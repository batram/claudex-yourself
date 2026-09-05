using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace ClaudexYourself;

internal static class CodexUpdates
{
    internal const string ManifestUrl = "https://persistent.oaistatic.com/codex-app-prod/windows-store-update.json";
    private static readonly string Root = Path.Combine(Program.StateDirectory, "updates");
    private static readonly string StatusPath = Path.Combine(Root, "status.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim StatusGate = new(1);
    internal sealed record Package(string FullName, string FamilyName, string Version, string Publisher, string Architecture, string Status, string? InstallLocation = null);
    internal sealed record Release(string BuildVersion, string PackageIdentity, string StoreProductId, int SchemaVersion);
    internal sealed record UpdateCheck(Package Installed, string AvailableVersion, bool UpdateAvailable, string PackageUrl);
    internal sealed record UpdateStatus(string State, string Message, string? TargetVersion = null, DateTime? UpdatedAtUtc = null, int? WorkerPid = null, DateTime? WorkerStartedUtc = null);

    public static async Task<int> CommandAsync(string[] arguments)
    {
        var action = arguments.FirstOrDefault() ?? "check";
        object result = action switch
        {
            "check" => await CheckAsync(),
            "prepare" => await PrepareCommandAsync(),
            "status" => await ReadStatusAsync(),
            "install" => await ScheduleAsync(),
            _ => throw new ArgumentException("update requires check, prepare, status, or install.")
        };
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        return 0;
    }

    public static async Task<UpdateCheck> CheckAsync()
    {
        RequireWindows();
        var installed = JsonSerializer.Deserialize<Package>(await WindowsAsync("inspect"), Json)
            ?? throw new InvalidOperationException("Windows returned no package metadata.");
        if (installed.Status != "Ok") throw new InvalidOperationException($"Codex package status is {installed.Status}; repair it in Windows before updating.");
        using var client = CreateClient(TimeSpan.FromSeconds(30));
        var release = JsonSerializer.Deserialize<Release>(await client.GetStringAsync(ManifestUrl), Json)
            ?? throw new InvalidOperationException("OpenAI returned an empty update manifest.");
        return ValidateRelease(installed, release);
    }

    internal static UpdateCheck ValidateRelease(Package installed, Release release)
    {
        if (release.SchemaVersion != 1 || release.PackageIdentity != "OpenAI.Codex" || release.StoreProductId != "9PLM9XGG6VKS")
            throw new InvalidOperationException("Unexpected OpenAI update manifest identity or schema.");
        if (installed.FamilyName != "OpenAI.Codex_2p2nqsd0c76g0" || installed.Architecture is not ("x64" or "arm64"))
            throw new InvalidOperationException("Only the stable x64/arm64 OpenAI.Codex Windows package is supported.");
        var current = ParseVersion(installed.Version);
        var latest = ParseVersion(release.BuildVersion);
        return new(installed, latest.ToString(), latest > current,
            // OpenAI's documented stable MSIX link. The native version-specific URL currently returns 404.
            // Validate the downloaded version against the release manifest before any staging or shutdown.
            new Uri(new Uri(ManifestUrl), $"ChatGPT-{installed.Architecture}.msix").AbsoluteUri);
    }

    internal static Version ParseVersion(string value)
    {
        if (!Version.TryParse(value, out var version) || version.Revision < 0 || version.ToString() != value
            || new[] { version.Major, version.Minor, version.Build, version.Revision }.Any(n => n > 65535))
            throw new InvalidOperationException($"Invalid Windows package version: {value}");
        return version;
    }

    public static async Task<UpdateStatus> ReadStatusAsync()
    {
        var status = File.Exists(StatusPath)
            ? JsonSerializer.Deserialize<UpdateStatus>(await File.ReadAllTextAsync(StatusPath), Json) ?? new("idle", "No update requested.")
            : new("idle", "No update requested.");
        if (status.WorkerPid is int workerPid && status.State is "checking" or "downloading" or "staging" or "closing" or "installing" or "restarting")
        {
            try
            {
                using var worker = Process.GetProcessById(workerPid);
                if (!worker.HasExited && worker.StartTime.ToUniversalTime() == status.WorkerStartedUtc) return status;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            return status with { State = "failed", Message = $"The updater stopped during {status.State}. Check the installed version before retrying." };
        }
        return status;
    }

    private static async Task<PreparedUpdate> PrepareCommandAsync()
    {
        try { return await PrepareAsync(await CheckAsync()); }
        catch (Exception exception) { await SetStatusAsync("failed", exception.Message); throw; }
    }

    public static async Task<object> ScheduleAsync()
    {
        RequireWindows();
        // Verify the quit bridge before starting a worker. Never fall back to killing the user's app.
        var readiness = await RendererDevTools.EvaluateStringAsync("JSON.stringify({ready:typeof window.electronBridge?.sendMessageFromView==='function'})", TimeSpan.FromSeconds(5));
        if (!JsonDocument.Parse(readiness).RootElement.GetProperty("ready").GetBoolean())
            throw new InvalidOperationException("The controlled Codex quit bridge is unavailable.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate updater executable.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use the published claudex-yourself executable to schedule updates.");
        var response = await WindowsAsync("detach", "-Launcher", executable);
        return new { state = "scheduled", worker = JsonSerializer.Deserialize<JsonElement>(response), message = "The updater will download and validate the package, quit Codex normally, install after exit, and relaunch in controlled mode." };
    }

    public static async Task<int> RunWorkerAsync()
    {
        RequireWindows();
        Directory.CreateDirectory(Root);
        // Held by a file handle, so all awaits are safe and a crash automatically releases it.
        using var operationLock = TryLock("install.lock");
        if (operationLock is null) return 0;
        string? target = null;
        var appExited = false;
        try
        {
            await SetStatusAsync("checking", "Checking for a Codex update.");
            var check = await CheckAsync();
            target = check.AvailableVersion;
            if (!check.UpdateAvailable) { await SetStatusAsync("current", "Codex is up to date.", target); return 0; }
            var prepared = await PrepareAsync(check);
            var packagePath = prepared.PackagePath ?? throw new InvalidOperationException("The update is no longer available.");
            await SetStatusAsync("closing", "Closing Codex. Accept its quit confirmation to continue; cancelling leaves the update uninstalled.", target);
            // Queue quit after acknowledging CDP, so a lost connection cannot be mistaken for a sent request.
            await RendererDevTools.EvaluateStringAsync("(() => { if(typeof window.electronBridge?.sendMessageFromView !== 'function') throw Error('Codex quit bridge unavailable'); setTimeout(() => window.electronBridge.sendMessageFromView({type:'quit-app'}), 250); return JSON.stringify({requested:true}); })()", TimeSpan.FromSeconds(5));
            await WaitForExitAsync(check.Installed.FamilyName, TimeSpan.FromSeconds(60));
            appExited = true;
            await SetStatusAsync("installing", "Codex has exited. Installing the update.", target);
            await InstallWithRetryAsync(check, prepared);
            var installed = JsonSerializer.Deserialize<Package>(await WindowsAsync("inspect"), Json)!;
            if (installed.Status != "Ok" || ParseVersion(installed.Version) < ParseVersion(target))
                throw new InvalidOperationException($"Windows did not complete registration: expected {target}, found {installed.Version}, status {installed.Status}.");
            await SetStatusAsync("restarting", $"Installed Codex {installed.Version}. Restarting in controlled mode.", target);
            var launched = Program.Launch(requireControlled: true);
            if (launched != 0) throw new InvalidOperationException("Update installed but controlled relaunch failed.");
            await SetStatusAsync("completed", $"Updated to Codex {installed.Version} and relaunched in controlled mode.", target);
            return 0;
        }
        catch (Exception exception)
        {
            await SetStatusAsync("failed", exception.Message, target);
            // Make a failure visible again if we already closed the app. Keep the failure status intact.
            if (appExited && exception is not TimeoutException)
            {
                try { Program.Launch(); }
                catch (Exception launchError) { await SetStatusAsync("failed", $"{exception.Message} Relaunch also failed: {launchError.Message}", target); }
            }
            return 1;
        }
    }

    internal sealed record PreparedUpdate(string State, string? PackagePath, string TargetVersion, bool AlreadyStaged = false);

    internal static async Task<PreparedUpdate> PrepareAsync(UpdateCheck check)
    {
        RequireWindows();
        if (!check.UpdateAvailable) return new("current", null, check.AvailableVersion);
        Directory.CreateDirectory(Root);
        using var preparationLock = TryLock("prepare.lock") ?? throw new InvalidOperationException("Another updater is preparing a package. Try again after it finishes.");
        var stagedManifest = FindStagedManifest(check);
        if (stagedManifest is not null)
        {
            await SetStatusAsync("ready", $"Windows has already staged Codex {check.AvailableVersion}. Ready to finish installation after Codex exits.", check.AvailableVersion);
            return new("ready", stagedManifest, check.AvailableVersion, AlreadyStaged: true);
        }
        var path = Path.Combine(Root, $"ChatGPT-{check.Installed.Architecture}-{check.AvailableVersion}.msix");
        var cached = false;
        if (File.Exists(path))
        {
            try { ValidatePackage(path, check); cached = true; }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException) { }
        }
        if (!cached)
        {
            await SetStatusAsync("downloading", $"Downloading Codex {check.AvailableVersion}.", check.AvailableVersion);
            await DownloadAsync(check, path);
            ValidatePackage(path, check);
        }
        await SetStatusAsync("staging", "Validating and staging the signed Windows package.", check.AvailableVersion);
        await WindowsAsync("stage", "-PackagePath", path);
        await SetStatusAsync("ready", "Update downloaded, validated, and staged. Codex has not been closed.", check.AvailableVersion);
        return new("ready", path, check.AvailableVersion);
    }

    private static async Task InstallWithRetryAsync(UpdateCheck check, PreparedUpdate prepared)
    {
        for (var attempt = 1; ; attempt++)
        {
            // The first attempt immediately follows the caller's verified quiet period.
            // Recheck if Windows rejected an attempt, or a process appeared in the meantime.
            if (attempt > 1 || PackageIsRunning(check.Installed.FamilyName))
                await WaitForExitAsync(check.Installed.FamilyName, TimeSpan.FromSeconds(15));
            try { await WindowsAsync(prepared.AlreadyStaged ? "register-staged" : "install", "-PackagePath", prepared.PackagePath!); return; }
            catch (Exception exception) when (attempt < 3 && IsPackageInUse(exception.Message))
            {
                await SetStatusAsync("installing", $"Windows is still releasing Codex; retry {attempt} of 2.", check.AvailableVersion);
                await Task.Delay(2000);
            }
        }
    }

    internal static bool IsPackageInUse(string error) => error.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase)
        || error.Contains("apps need to be closed", StringComparison.OrdinalIgnoreCase);

    private static async Task DownloadAsync(UpdateCheck check, string path)
    {
        var temporary = path + ".partial";
        try
        {
            using var client = CreateClient(TimeSpan.FromMinutes(20));
            using var response = await client.GetAsync(check.PackageUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            const long limit = 2L * 1024 * 1024 * 1024;
            var length = response.Content.Headers.ContentLength;
            if (length is <= 0 or > limit) throw new IOException("Unexpected MSIX download size.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            await using (var inputStream = await response.Content.ReadAsStreamAsync(timeout.Token))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                var progress = Stopwatch.StartNew();
                int count;
                while ((count = await inputStream.ReadAsync(buffer, timeout.Token)) != 0)
                {
                    total += count;
                    if (total > limit) throw new IOException("MSIX exceeds the download limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    if (progress.Elapsed >= TimeSpan.FromSeconds(1))
                    {
                        var detail = length.HasValue ? $"{total * 100 / length.Value}% ({total / 1048576} of {length.Value / 1048576} MB)" : $"{total / 1048576} MB";
                        await SetStatusAsync("downloading", $"Downloading Codex {check.AvailableVersion}: {detail}.", check.AvailableVersion);
                        progress.Restart();
                    }
                }
                if (length.HasValue && total != length) throw new IOException("Incomplete MSIX download.");
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void ValidatePackage(string path, UpdateCheck check)
    {
        using var archive = ZipFile.OpenRead(path);
        var manifest = archive.GetEntry("AppxManifest.xml") ?? throw new InvalidOperationException("MSIX has no manifest.");
        if (manifest.Length > 1024 * 1024) throw new InvalidOperationException("MSIX manifest is too large.");
        using var stream = manifest.Open();
        ValidateIdentity(stream, check);
    }

    internal static string? FindStagedManifest(UpdateCheck check)
    {
        // Derive the sibling from Windows' registered package location, never from a downloaded path.
        if (string.IsNullOrEmpty(check.Installed.InstallLocation)) return null;
        var parent = Path.GetDirectoryName(check.Installed.InstallLocation);
        if (parent is null) return null;
        var name = $"OpenAI.Codex_{check.AvailableVersion}_{check.Installed.Architecture}__2p2nqsd0c76g0";
        var manifest = Path.Combine(parent, name, "AppxManifest.xml");
        if (!File.Exists(manifest)) return null;
        using var stream = File.OpenRead(manifest);
        ValidateIdentity(stream, check);
        return manifest;
    }

    private static void ValidateIdentity(Stream stream, UpdateCheck check)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var doc = XDocument.Load(reader);
        var identity = doc.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Identity");
        if ((string?)identity?.Attribute("Name") != "OpenAI.Codex"
            || (string?)identity?.Attribute("Publisher") != check.Installed.Publisher
            || (string?)identity?.Attribute("Version") != check.AvailableVersion
            || (string?)identity?.Attribute("ProcessorArchitecture") != check.Installed.Architecture)
            throw new InvalidOperationException($"MSIX does not match the requested OpenAI update. Expected version {check.AvailableVersion}, architecture {check.Installed.Architecture}; received name {(string?)identity?.Attribute("Name")}, version {(string?)identity?.Attribute("Version")}, architecture {(string?)identity?.Attribute("ProcessorArchitecture")}. Publisher must also match the installed package. Codex has not been closed.");
    }

    internal static async Task WaitForQuietAsync(Func<bool> isRunning, TimeSpan timeout, TimeSpan quietPeriod, TimeSpan pollInterval)
    {
        var elapsed = Stopwatch.StartNew();
        TimeSpan? quietSince = null;
        while (elapsed.Elapsed < timeout)
        {
            if (isRunning()) quietSince = null;
            else
            {
                quietSince ??= elapsed.Elapsed;
                if (elapsed.Elapsed - quietSince >= quietPeriod) return;
            }
            await Task.Delay(pollInterval);
        }
        throw new TimeoutException("Codex did not fully exit. The quit confirmation may have been cancelled, or a package process is still running. No update was installed.");
    }

    private static Task WaitForExitAsync(string family, TimeSpan timeout) => WaitForQuietAsync(
        () => PackageIsRunning(family), timeout, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(250));

    private static bool PackageIsRunning(string family)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                using var handle = OpenProcess(0x1000, false, process.Id);
                if (handle.IsInvalid)
                {
                    // Never assume an inaccessible Codex desktop/backend has exited.
                    try { if (process.ProcessName is "ChatGPT" or "Codex" or "codex") return true; }
                    catch (InvalidOperationException) { }
                    continue;
                }
                uint size = 0;
                var result = GetPackageFamilyName(handle, ref size, null);
                if (result == 15700) continue; // APPMODEL_ERROR_NO_PACKAGE
                if (result == 122)
                {
                    var name = new StringBuilder((int)size);
                    if (GetPackageFamilyName(handle, ref size, name) == 0 && name.ToString() == family) return true;
                }
            }
        }
        return false;
    }

    private static HttpClient CreateClient(TimeSpan timeout) => new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = timeout };

    private static FileStream? TryLock(string name)
    {
        try { return new FileStream(Path.Combine(Root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return null; }
    }

    private static async Task<string> WindowsAsync(string action, params string[] arguments)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "update", "WindowsPackage.ps1");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-Action", action }.Concat(arguments)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Windows package helper.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Windows package {action} exceeded 20 minutes. The Windows helper (PID {process.Id}) may still be working; Codex will not be relaunched automatically. Check Windows deployment status before reopening it.");
        }
        var stderr = await error;
        if (process.ExitCode != 0) throw new InvalidOperationException($"Windows package {action} failed: {stderr.Trim()}");
        return (await output).Trim();
    }

    private static async Task SetStatusAsync(string state, string message, string? target = null)
    {
        Directory.CreateDirectory(Root);
        await StatusGate.WaitAsync();
        try
        {
            var temporary = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using var current = Process.GetCurrentProcess();
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new UpdateStatus(state, message, target, DateTime.UtcNow, current.Id, current.StartTime.ToUniversalTime()), Json));
            File.Move(temporary, StatusPath, overwrite: true);
        }
        finally { StatusGate.Release(); }
    }

    public static void StartWatcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("update-watch");
        Process.Start(start)?.Dispose();
    }

    public static async Task<int> WatchAsync()
    {
        RequireWindows();
        Directory.CreateDirectory(Root);
        FileStream? acquired = null;
        for (var attempt = 0; attempt < 10 && acquired is null; attempt++)
        {
            acquired = TryLock("watch.lock");
            if (acquired is null) await Task.Delay(500);
        }
        using var watcherLock = acquired;
        if (watcherLock is null) return 0;
        var source = await File.ReadAllTextAsync(Path.Combine(Program.BundledScriptDirectory, "codex_updates.js"));
        var nextCheck = DateTime.MinValue;
        UpdateCheck? check = null;
        string? checkError = null;
        Task<UpdateCheck>? checking = null;
        DateTime? checkFinishedAtUtc = null;
        var missed = 0;
        while (missed < 30)
        {
            try
            {
                var action = await RendererDevTools.EvaluateStringAsync("JSON.stringify(window[Symbol.for('claudex-yourself.codex-updates')]?.takeAction() ?? 'missing')", TimeSpan.FromSeconds(3));
                if (action == "\"missing\"") await Program.RunScriptAsync(source, "codex_updates");
                if (action == "\"install\"")
                {
                    try { await ScheduleAsync(); }
                    catch (Exception exception) { await SetStatusAsync("failed", exception.Message); }
                }
                if (action == "\"check\"") nextCheck = DateTime.MinValue;
                if (checking is null && DateTime.UtcNow >= nextCheck)
                    checking = CheckAsync();
                if (checking?.IsCompleted == true)
                {
                    try { check = await checking; checkError = null; }
                    catch (Exception exception) { checkError = exception.Message; }
                    checking = null;
                    checkFinishedAtUtc = DateTime.UtcNow;
                    nextCheck = DateTime.UtcNow.AddMinutes(15);
                }
                var state = await ReadStatusAsync();
                var payload = JsonSerializer.Serialize(new { check, checkError, checkRunning = checking is not null, checkFinishedAtUtc, operation = state }, Json);
                await RendererDevTools.EvaluateStringAsync($"JSON.stringify(window[Symbol.for('claudex-yourself.codex-updates')]?.update({payload}) ?? null)", TimeSpan.FromSeconds(3));
                missed = 0;
            }
            catch (Exception exception)
            {
                if ((await ReadStatusAsync()).State is "closing" or "installing" or "restarting") return 0;
                missed++;
                // Persist bridge errors only after startup retries; transient renderer reloads are expected.
                if (missed == 30) await File.WriteAllTextAsync(Path.Combine(Root, "watch-error.txt"), exception.Message);
            }
            await Task.Delay(500);
        }
        return 1;
    }

    private static void RequireWindows()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Claudex-managed updates currently support Windows only.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeProcessHandle OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackageFamilyName(Microsoft.Win32.SafeHandles.SafeProcessHandle process, ref uint length, StringBuilder? familyName);
}

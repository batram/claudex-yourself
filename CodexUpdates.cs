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
    internal sealed record UpdateCheck(Package Installed, string AvailableVersion, bool UpdateAvailable, string PackageUrl, string? DownloadBlockedReason = null,
        string? AnnouncedVersion = null, string? Source = null, string? SourceWarning = null, string? CachedPackagePath = null,
        StoreAvailability? Store = null);
    internal sealed record StoreAvailability(bool HasUpdate, bool CanSilentlyDownload, bool Completed = false, string? OverallState = null);
    internal sealed record UpdateCandidate(string Version, string PackageUrl, string Source, string? CachedPackagePath = null);
    internal sealed record DownloadReceipt(string Url, string? ETag, long Length, DateTime LastWriteTimeUtc);
    private sealed class UpdateNotReadyException(string message) : InvalidOperationException(message);
    internal sealed record UpdateStatus(string State, string Message, string? TargetVersion = null, DateTime? UpdatedAtUtc = null, int? WorkerPid = null, DateTime? WorkerStartedUtc = null, string? FailureKind = null);

    public static async Task<int> CommandAsync(string[] arguments)
    {
        var action = arguments.FirstOrDefault() ?? "check";
        object result = action switch
        {
            "check" => await CheckAsync(),
            "prepare" => await PrepareCommandAsync(),
            "status" => await ReadStatusAsync(),
            "install" => await ScheduleAsync(),
            "restart-test" => await ScheduleAsync(mockUpdate: true),
            _ => throw new ArgumentException("update requires check, prepare, status, install, or restart-test.")
        };
        Console.WriteLine(JsonSerializer.Serialize(result, Json));
        return 0;
    }

    public static async Task<UpdateCheck> CheckAsync(bool includeStore = true)
    {
        RequireWindows();
        var installed = JsonSerializer.Deserialize<Package>(await WindowsAsync("inspect"), Json)
            ?? throw new InvalidOperationException("Windows returned no package metadata.");
        if (installed.Status != "Ok") throw new InvalidOperationException($"Codex package status is {installed.Status}; repair it in Windows before updating.");
        using var client = CreateClient(TimeSpan.FromSeconds(30));
        string? manifestWarning = null;
        Release release;
        try
        {
            release = JsonSerializer.Deserialize<Release>(await client.GetStringAsync(ManifestUrl), Json)
                ?? throw new InvalidOperationException("OpenAI returned an empty update manifest.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            manifestWarning = $"Could not read the release announcement: {exception.Message}";
            release = new(installed.Version, "OpenAI.Codex", "9PLM9XGG6VKS", 1);
        }
        var check = ValidateRelease(installed, release);
        var local = ReadCachedCandidates(check, Root).ToList();
        string? stagedWarning = null;
        var packageNames = new List<string> { $"OpenAI.Codex_{check.AvailableVersion}_{installed.Architecture}__2p2nqsd0c76g0" };
        try
        {
            packageNames.AddRange(JsonSerializer.Deserialize<string[]>(await WindowsAsync("staged-candidates"), Json) ?? []);
        }
        catch (InvalidOperationException exception)
        {
            stagedWarning = $"Could not check Windows deployment history: {exception.Message}";
        }
        local.AddRange(ReadStagedCandidates(check, packageNames));
        check = await ResolveAvailableAsync(client, check, local);
        if (stagedWarning is not null)
            check = check with { SourceWarning = string.Join(" ", new[] { check.SourceWarning, stagedWarning }.Where(s => s is not null)) };
        if (check.UpdateAvailable && check.Source != "windows-staged")
            check = check with { DownloadBlockedReason = await GetDownloadBlockAsync(client, check, check.CachedPackagePath ?? CachePath(check)) };
        if (manifestWarning is not null) check = AddSourceWarning(check, manifestWarning);
        if (includeStore)
        {
            try
            {
                var store = JsonSerializer.Deserialize<StoreAvailability>(await WindowsAsync("store-check"), Json)
                    ?? throw new InvalidOperationException("Microsoft Store returned no availability information.");
                check = WithStoreAvailability(check, store);
            }
            catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
            {
                check = AddSourceWarning(check, $"Could not check Microsoft Store: {exception.Message}");
            }
        }
        return check;
    }

    private static UpdateCheck AddSourceWarning(UpdateCheck check, string warning) => check with {
        SourceWarning = string.Join(" ", new[] { check.SourceWarning, warning }.Where(s => !string.IsNullOrEmpty(s)))
    };

    // StorePackageUpdate.Package describes the installed package. Never use its
    // version, or the public announcement, as a verified Store target version.
    internal static UpdateCheck WithStoreAvailability(UpdateCheck check, StoreAvailability store) => check with {
        Store = store,
        UpdateAvailable = check.UpdateAvailable || store.HasUpdate && store.CanSilentlyDownload,
        DownloadBlockedReason = store.HasUpdate && store.CanSilentlyDownload ? null : check.DownloadBlockedReason
    };

    private static string VersionedPackageUrl(UpdateCheck check) => new Uri(new Uri(ManifestUrl),
        $"releases/{check.AvailableVersion}/ChatGPT-{check.Installed.Architecture}.msix").AbsoluteUri;

    internal static async Task<UpdateCheck> ResolveAvailableAsync(HttpClient client, UpdateCheck announced, IEnumerable<UpdateCandidate> local)
    {
        async Task<(UpdateCandidate? Candidate, string? Warning)> ProbeAsync(string url, string source, string? expectedVersion)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Head, url);
                request.Headers.CacheControl = new() { NoCache = true };
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return (null, null);
                response.EnsureSuccessStatusCode();
                var version = response.Headers.TryGetValues("x-ms-meta-package_version", out var values) ? values.FirstOrDefault() : expectedVersion;
                if (version is null) return (null, $"The {source} download did not identify its version.");
                ParseVersion(version);
                if (expectedVersion is not null && version != expectedVersion) return (null, "The version-specific download identifies a different version.");
                var candidate = announced with { AvailableVersion = version, PackageUrl = url };
                var blocked = DownloadHeaderBlock(response, candidate);
                return blocked is null ? (new(version, url, source), null) : (null, blocked);
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidOperationException)
            {
                return (null, $"Could not check the {source} download: {exception.Message}");
            }
        }
        var probes = await Task.WhenAll(
            ProbeAsync(VersionedPackageUrl(announced), "version-specific", announced.AvailableVersion),
            ProbeAsync(announced.PackageUrl, "stable", null));
        var candidates = local.Concat(probes.Where(p => p.Candidate is not null).Select(p => p.Candidate!))
            .Append(new(announced.Installed.Version, announced.PackageUrl, "installed"));
        var best = candidates.OrderByDescending(c => ParseVersion(c.Version))
            .ThenBy(c => c.Source == "windows-staged" ? 0 : c.CachedPackagePath is not null ? 1 : 2).First();
        var warnings = probes.Where(p => p.Warning is not null).Select(p => p.Warning!).ToList();
        if (probes.All(p => p.Candidate is null) && warnings.Count == 0)
            warnings.Add("Neither public download endpoint currently provides an installer to check.");
        return announced with {
            AvailableVersion = best.Version, UpdateAvailable = ParseVersion(best.Version) > ParseVersion(announced.Installed.Version),
            PackageUrl = best.PackageUrl, AnnouncedVersion = announced.AvailableVersion, Source = best.Source,
            SourceWarning = warnings.Count == 0 ? null : string.Join(" ", warnings), CachedPackagePath = best.CachedPackagePath,
            DownloadBlockedReason = null
        };
    }

    internal static IEnumerable<UpdateCandidate> ReadCachedCandidates(UpdateCheck announced, string directory)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, $"ChatGPT-{announced.Installed.Architecture}-*.msix"))
        {
            UpdateCandidate? candidate = null;
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var manifest = archive.GetEntry("AppxManifest.xml");
                if (manifest is null || manifest.Length > 1024 * 1024) continue;
                using var stream = manifest.Open();
                var identity = ReadIdentity(stream);
                var version = (string?)identity?.Attribute("Version") ?? "";
                ParseVersion(version);
                ValidateIdentity(identity, announced with { AvailableVersion = version });
                candidate = new(version, announced.PackageUrl, "cache", path);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or InvalidDataException or XmlException) { }
            if (candidate is not null) yield return candidate;
        }
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
            // Documented direct-MSIX deployment link; Store announcements can precede this asset.
            // The announcement is a discovery hint; resolve actual candidates before installation.
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

    private static async Task ClearRecoveredRelaunchFailureAsync()
    {
        var status = await ReadStatusAsync();
        if (!IsRelaunchFailure(status)) return;
        // Never clear a failure while an installation worker still owns the operation.
        using var operationLock = TryLock("install.lock");
        if (operationLock is null) return;
        status = await ReadStatusAsync();
        var recovered = await RecoverRelaunchStatusAsync(status, UserscriptMetadata.CurrentCodexVersion(), async () =>
            await RendererDevTools.EvaluateStringAsync("JSON.stringify(document.readyState === 'complete' && Boolean(document.body) && typeof window.electronBridge?.sendMessageFromView === 'function')", TimeSpan.FromSeconds(3)) == "true");
        if (recovered != status)
            await SetStatusAsync(recovered.State, recovered.Message, recovered.TargetVersion);
    }

    private static bool IsRelaunchFailure(UpdateStatus status) => status.State == "failed" &&
        (status.FailureKind == "controlled-relaunch" ||
         // Preserve recovery for status files written before structured failure kinds.
         status.FailureKind is null && (status.Message.Contains("controlled renderer did not become ready", StringComparison.OrdinalIgnoreCase) ||
             status.Message.Contains("controlled Codex renderer did not become ready", StringComparison.OrdinalIgnoreCase) ||
             status.Message == "The updater stopped during restarting. Check the installed version before retrying."));

    internal static async Task<UpdateStatus> RecoverRelaunchStatusAsync(UpdateStatus status, string installedVersion, Func<Task<bool>> ready)
    {
        if (!IsRelaunchFailure(status) || !Version.TryParse(status.TargetVersion, out var target) ||
            !Version.TryParse(installedVersion, out var installed) || installed < target || !await ready()) return status;
        return status with { State = "completed", Message = $"Codex {installedVersion} is running in controlled mode after the interrupted relaunch.", FailureKind = null };
    }

    private static async Task<PreparedUpdate> PrepareCommandAsync()
    {
        try { return await PrepareAsync(await CheckAsync()); }
        catch (Exception exception) { await SetStatusAsync(exception is UpdateNotReadyException ? "waiting" : "failed", exception.Message); throw; }
    }

    public static async Task<object> ScheduleAsync(bool mockUpdate = false)
    {
        RequireWindows();
        // A normal quit and verified exit are required before any installation/recovery attempt.
        var readiness = await RendererDevTools.EvaluateStringAsync("JSON.stringify({ready:typeof window.electronBridge?.sendMessageFromView==='function'})", TimeSpan.FromSeconds(5));
        if (!JsonDocument.Parse(readiness).RootElement.GetProperty("ready").GetBoolean())
            throw new InvalidOperationException("The controlled Codex quit bridge is unavailable.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate updater executable.");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use the published claudex-yourself executable to schedule updates.");
        var response = await WindowsAsync("detach", mockUpdate ? ["-Launcher", executable, "-MockUpdate"] : ["-Launcher", executable]);
        return new { state = "scheduled", worker = JsonSerializer.Deserialize<JsonElement>(response), message = mockUpdate
            ? "The mock update test will quit Codex normally and relaunch in controlled mode. No package will be downloaded or installed."
            : "The updater will download and validate the package, quit Codex normally, install after exit, and relaunch in controlled mode." };
    }

    public static async Task<int> RunWorkerAsync(bool mockUpdate = false)
    {
        RequireWindows();
        Directory.CreateDirectory(Root);
        // Held by a file handle, so all awaits are safe and a crash automatically releases it.
        using var operationLock = TryLock("install.lock");
        if (operationLock is null) return 0;
        string? target = null;
        var appExited = false;
        var restarting = false;
        try
        {
            await SetStatusAsync("checking", mockUpdate ? "Mock update restart test: inspecting the installed package; no download or installation will run." : "Checking for a Codex update.");
            UpdateCheck check;
            if (mockUpdate)
            {
                var current = JsonSerializer.Deserialize<Package>(await WindowsAsync("inspect"), Json)
                    ?? throw new InvalidOperationException("Windows returned no package metadata.");
                if (current.Status != "Ok") throw new InvalidOperationException($"Codex package status is {current.Status}.");
                check = new(current, current.Version, true, "");
            }
            else check = await CheckAsync();
            target = check.AvailableVersion;
            if (!check.UpdateAvailable)
            {
                var unavailable = UnavailableUpdateStatus(check);
                await SetStatusAsync(unavailable.State, unavailable.Message, target);
                return unavailable.State == "current" ? 0 : 1;
            }
            var prepared = mockUpdate ? new PreparedUpdate("ready", null, check.Installed.Version) : await PrepareAsync(check);
            target = prepared.TargetVersion;
            check = check with { AvailableVersion = target };
            if (!mockUpdate && prepared.PackagePath is null) throw new InvalidOperationException("The update is no longer available.");
            await SetStatusAsync("closing", "Closing Codex. Accept its quit confirmation to continue; cancelling leaves the update uninstalled.", target);
            // Queue quit after acknowledging CDP, so a lost connection cannot be mistaken for a sent request.
            await RendererDevTools.EvaluateStringAsync("(() => { if(typeof window.electronBridge?.sendMessageFromView !== 'function') throw Error('Codex quit bridge unavailable'); setTimeout(() => window.electronBridge.sendMessageFromView({type:'quit-app'}), 250); return JSON.stringify({requested:true}); })()", TimeSpan.FromSeconds(5));
            await WaitForExitAsync(check.Installed.FamilyName, TimeSpan.FromSeconds(60));
            appExited = true;
            await SetStatusAsync("installing", mockUpdate ? "Mock update restart test: Codex has fully exited. Skipping package installation." : "Codex has exited. Installing the update.", target);
            if (!mockUpdate) await InstallWithRetryAsync(check, prepared);
            var installed = JsonSerializer.Deserialize<Package>(await WindowsAsync("inspect"), Json)!;
            if (installed.Status != "Ok" || ParseVersion(installed.Version) < ParseVersion(target) || mockUpdate && installed.Version != target)
                throw new InvalidOperationException($"Windows did not complete registration: expected {target}, found {installed.Version}, status {installed.Status}.");
            await SetStatusAsync("restarting", mockUpdate ? $"Mock update restart test: reopening unchanged Codex {installed.Version} in controlled mode." : $"Installed Codex {installed.Version}. Restarting in controlled mode.", target);
            restarting = true;
            var launched = Program.Launch(requireControlled: true);
            if (launched != 0) throw new InvalidOperationException("Update installed but controlled relaunch failed.");
            await SetStatusAsync("completed", mockUpdate ? $"Mock update restart test completed: Codex {installed.Version} exited and relaunched in controlled mode. No package was downloaded or installed." : $"Updated to Codex {installed.Version} and relaunched in controlled mode.", target);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            await SetStatusAsync(exception is UpdateNotReadyException ? "waiting" : "failed", exception.Message, target, restarting ? "controlled-relaunch" : null);
            // Make a failure visible again if we already closed the app. Keep the failure status intact.
            if (appExited && exception is not TimeoutException)
            {
                try { Program.Launch(); }
                catch (Exception launchError) { await SetStatusAsync("failed", $"{exception.Message} Relaunch also failed: {launchError.Message}", target, restarting ? "controlled-relaunch" : null); }
            }
            return 1;
        }
    }

    internal sealed record PreparedUpdate(string State, string? PackagePath, string TargetVersion, bool AlreadyStaged = false);

    internal static UpdateStatus UnavailableUpdateStatus(UpdateCheck check)
    {
        var announcedNewer = check.AnnouncedVersion is not null && ParseVersion(check.AnnouncedVersion) > ParseVersion(check.Installed.Version);
        if (announcedNewer || check.Store?.HasUpdate == true)
            return new("waiting", $"The update could not be prepared: Codex {check.Installed.Version} is still installed, but no newer package is currently obtainable. The Store download or staged package may have changed since the panel checked. Codex has been left open. Check again to refresh availability. {check.SourceWarning}".TrimEnd());
        return new("current", check.SourceWarning is null
            ? $"Codex {check.Installed.Version} is the newest version found through the checked sources."
            : $"No newer verified package was found. {check.SourceWarning}");
    }

    internal static async Task<PreparedUpdate> PrepareAsync(UpdateCheck check)
    {
        RequireWindows();
        if (!check.UpdateAvailable) return new("current", null, check.AvailableVersion);
        Directory.CreateDirectory(Root);
        using var preparationLock = TryLock("prepare.lock") ?? throw new InvalidOperationException("Another updater is preparing a package. Try again after it finishes.");
        if (check.Store is { HasUpdate: true, CanSilentlyDownload: true })
        {
            // Store does not expose the target version before download. Resolve
            // again afterward and compare actual protected manifests with BOTH
            // direct endpoints, even if the announcement has moved or is older.
            await SetStatusAsync("downloading", "Downloading the Microsoft Store update. Its version will be verified and compared with direct downloads before Codex closes.");
            check = await ResolveAfterStoreDownloadAsync(async () =>
            {
                var result = JsonSerializer.Deserialize<StoreAvailability>(await WindowsAsync("store-download"), Json);
                if (result is null || result.HasUpdate && !result.Completed)
                    throw new InvalidOperationException("Microsoft Store did not complete the update download.");
            }, () => CheckAsync(includeStore: false));
            if (!check.UpdateAvailable)
                throw new UpdateNotReadyException(check.SourceWarning ?? "Microsoft Store did not expose a newer verified staged package. Check again when the native download finishes.");
        }
        var stagedManifest = FindStagedManifest(check);
        if (stagedManifest is not null)
        {
            await SetStatusAsync("ready", $"Windows has already staged Codex {check.AvailableVersion}. Ready to finish installation after Codex exits. {check.SourceWarning}".TrimEnd(), check.AvailableVersion);
            return new("ready", stagedManifest, check.AvailableVersion, AlreadyStaged: true);
        }
        var path = check.CachedPackagePath ?? CachePath(check);
        var cached = false;
        if (File.Exists(path))
        {
            try { ValidatePackage(path, check); cached = true; }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or XmlException) { }
        }
        if (!cached)
        {
            using var client = CreateClient(TimeSpan.FromSeconds(30));
            // Repeat the lightweight check at the point of use, even for direct CLI/MCP requests.
            var blocked = await GetDownloadBlockAsync(client, check, path);
            if (blocked is not null) throw new UpdateNotReadyException(blocked);
            await SetStatusAsync("downloading", $"Downloading Codex {check.AvailableVersion}.", check.AvailableVersion);
            await DownloadAsync(check, path);
            try { ValidatePackage(path, check); }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or XmlException)
            {
                throw new UpdateNotReadyException($"The downloaded package was rejected: {exception.Message} Check again will inspect the server before another download.");
            }
        }
        await SetStatusAsync("staging", "Validating and staging the signed Windows package. If Windows requests administrator approval, approve it using this Windows account to continue.", check.AvailableVersion);
        await WindowsAsync("stage", "-PackagePath", path);
        await SetStatusAsync("ready", $"Update downloaded, validated, and staged. Codex has not been closed. {check.SourceWarning}".TrimEnd(), check.AvailableVersion);
        return new("ready", path, check.AvailableVersion);
    }

    internal static async Task<UpdateCheck> ResolveAfterStoreDownloadAsync(Func<Task> download, Func<Task<UpdateCheck>> resolve)
    {
        string? warning = null;
        try { await download(); }
        catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
        {
            warning = $"Microsoft Store download failed: {exception.Message}";
        }
        var resolved = await resolve();
        // Re-resolution must report verified packages, without the Store hint.
        if (resolved.Store is not null) throw new InvalidOperationException("Store preparation must resolve verified package versions.");
        return warning is null ? resolved : AddSourceWarning(resolved, warning);
    }

    private static async Task InstallWithRetryAsync(UpdateCheck check, PreparedUpdate prepared)
    {
        await RetryPackageInstallAsync(async finishTargetShutdown =>
        {
            // The first attempt immediately follows the caller's verified quiet period.
            // Never bypass quit cancellation, even when Windows reported package activity.
            if (finishTargetShutdown || PackageIsRunning(check.Installed.FamilyName))
                await WaitForExitAsync(check.Installed.FamilyName, TimeSpan.FromSeconds(15));
            var arguments = new List<string> { "-PackagePath", prepared.PackagePath! };
            if (finishTargetShutdown) arguments.Add("-FinishTargetShutdown");
            await WindowsAsync(prepared.AlreadyStaged ? "register-staged" : "install", arguments.ToArray());
        }, async attempt =>
        {
            await SetStatusAsync("installing", $"Codex has exited, but Windows still reports its package in use. Asking Windows to finish closing Codex package activity; retry {attempt} of 2.", check.AvailableVersion);
            await Task.Delay(2000);
        });
    }

    internal static async Task RetryPackageInstallAsync(Func<bool, Task> install, Func<int, Task> retrying)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { await install(attempt > 1); return; }
            catch (Exception exception) when (attempt < 3 && IsPackageInUse(exception.Message))
            {
                await retrying(attempt);
            }
        }
    }

    internal static bool IsPackageInUse(string error) => error.Contains("0x80073D02", StringComparison.OrdinalIgnoreCase)
        || error.Contains("0x800700E9", StringComparison.OrdinalIgnoreCase)
        || error.Contains("apps need to be closed", StringComparison.OrdinalIgnoreCase);

    private static string CachePath(UpdateCheck check) => Path.Combine(Root, $"ChatGPT-{check.Installed.Architecture}-{check.AvailableVersion}.msix");

    internal static string? DownloadHeaderBlock(HttpResponseMessage response, UpdateCheck check)
    {
        string? Header(string name) => response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
        var version = Header("x-ms-meta-package_version");
        if (version is not null && version != check.AvailableVersion)
            return $"The selected Codex {check.AvailableVersion} download now identifies version {version}. No installer will be downloaded. Use Check again to select the newest available package.";
        var identity = Header("x-ms-meta-package_identity");
        var architecture = Header("x-ms-meta-architecture");
        if ((identity is not null && identity != "OpenAI.Codex") || (architecture is not null && architecture != check.Installed.Architecture))
            return "OpenAI's download headers do not match the required Codex package identity or architecture. Download paused; use Check again later.";
        return null;
    }

    internal static async Task<string?> GetDownloadBlockAsync(HttpClient client, UpdateCheck check, string path)
    {
        try { return await GetDownloadBlockCoreAsync(client, check, path); }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            // A direct endpoint race/outage must not prevent the independent
            // Store check. Preparation will re-resolve before using a package.
            return $"Could not verify the direct download: {exception.Message} Use Check again to retry.";
        }
    }

    private static async Task<string?> GetDownloadBlockCoreAsync(HttpClient client, UpdateCheck check, string path)
    {
        var rejectedCache = false;
        if (File.Exists(path))
        {
            try { ValidatePackage(path, check); return null; }
            catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or XmlException) { rejectedCache = true; }
        }
        using var request = new HttpRequestMessage(HttpMethod.Head, check.PackageUrl);
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var blocked = DownloadHeaderBlock(response, check);
        if (blocked is not null) return blocked;
        if (!rejectedCache) return null;

        DownloadReceipt? receipt = null;
        var receiptPath = path + ".http.json";
        if (File.Exists(receiptPath))
        {
            try { receipt = JsonSerializer.Deserialize<DownloadReceipt>(await File.ReadAllTextAsync(receiptPath), Json); }
            catch (JsonException) { }
        }
        var file = new FileInfo(path);
        var tiedToFile = receipt?.Url == check.PackageUrl && receipt.Length == file.Length && receipt.LastWriteTimeUtc == file.LastWriteTimeUtc;
        var etag = response.Headers.ETag?.ToString();
        if (tiedToFile && receipt!.ETag is not null && etag == receipt.ETag)
            return "OpenAI's download is unchanged from the package already downloaded and rejected. No repeat download will be started. Use Check again later.";
        if (tiedToFile && (receipt!.ETag is null || etag is null))
            return "The downloaded package was rejected and the server provides no validator to confirm a replacement. No repeat download will be started. Use Check again later.";
        // Older cache entries have no receipt. Require positive evidence of a new candidate,
        // rather than downloading the same hundreds of megabytes on every retry.
        var versionKnown = response.Headers.TryGetValues("x-ms-meta-package_version", out var versions) && versions.FirstOrDefault() == check.AvailableVersion;
        var changedETag = tiedToFile && receipt!.ETag is not null && etag is not null && etag != receipt.ETag;
        return versionKnown || changedETag ? null
            : "The cached package was rejected and the server has not identified a replacement. No repeat download will be started. Use Check again later.";
    }

    private static async Task DownloadAsync(UpdateCheck check, string path)
    {
        var temporary = path + ".partial";
        try
        {
            using var client = CreateClient(TimeSpan.FromMinutes(20));
            using var response = await client.GetAsync(check.PackageUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            // The source can change between HEAD and GET. Recheck before reading the body.
            var blocked = DownloadHeaderBlock(response, check);
            if (blocked is not null) throw new UpdateNotReadyException(blocked);
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
            var file = new FileInfo(path);
            await File.WriteAllTextAsync(path + ".http.json", JsonSerializer.Serialize(new DownloadReceipt(check.PackageUrl,
                response.Headers.ETag?.ToString(), file.Length, file.LastWriteTimeUtc), Json));
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

    internal static IEnumerable<UpdateCandidate> ReadStagedCandidates(UpdateCheck check, IEnumerable<string> packageNames)
    {
        if (string.IsNullOrEmpty(check.Installed.InstallLocation)) yield break;
        var parent = Path.GetDirectoryName(check.Installed.InstallLocation);
        if (parent is null || !Directory.Exists(parent)) yield break;
        // Store delivery can stage an intermediate release while the announcement advances.
        // Deployment records are discovery hints only. Validate the real manifest in
        // Windows' protected package root; never use a path supplied by an event.
        var suffix = $"_{check.Installed.Architecture}__2p2nqsd0c76g0";
        foreach (var name in packageNames.Distinct())
        {
            if (!name.StartsWith("OpenAI.Codex_", StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)
                || name.Length <= "OpenAI.Codex_".Length + suffix.Length) continue;
            var version = name["OpenAI.Codex_".Length..^suffix.Length];
            UpdateCandidate? candidate = null;
            try
            {
                if (ParseVersion(version) <= ParseVersion(check.Installed.Version)) continue;
                var staged = check with { AvailableVersion = version };
                if (FindStagedManifest(staged) is not null)
                    candidate = new(version, VersionedPackageUrl(staged), "windows-staged");
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or XmlException) { }
            if (candidate is not null) yield return candidate;
        }
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

    private static XElement? ReadIdentity(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var doc = XDocument.Load(reader);
        return doc.Root?.Elements().SingleOrDefault(e => e.Name.LocalName == "Identity");
    }

    private static void ValidateIdentity(Stream stream, UpdateCheck check) => ValidateIdentity(ReadIdentity(stream), check);

    private static void ValidateIdentity(XElement? identity, UpdateCheck check)
    {
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

    internal static Task WaitForExitAsync(string family, TimeSpan timeout) => WaitForQuietAsync(
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

    private static async Task SetStatusAsync(string state, string message, string? target = null, string? failureKind = null)
    {
        Directory.CreateDirectory(Root);
        await StatusGate.WaitAsync();
        try
        {
            var temporary = StatusPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using var current = Process.GetCurrentProcess();
            var status = new UpdateStatus(state, message, target, DateTime.UtcNow, current.Id, current.StartTime.ToUniversalTime(), failureKind);
            var serialized = JsonSerializer.Serialize(status, Json);
            await File.WriteAllTextAsync(temporary, serialized);
            File.Move(temporary, StatusPath, overwrite: true);
            // Preserve attempt history: a later check/retry must not erase the
            // evidence needed to diagnose a failed shutdown or relaunch.
            Console.Error.WriteLine(serialized);
            await File.AppendAllTextAsync(Path.Combine(Root, "history.jsonl"), serialized + Environment.NewLine);
        }
        finally { StatusGate.Release(); }
    }

    public static void StartWatcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        BackgroundWorker.Start("update-watch").Dispose();
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
        Program.RestoreWindowsTaskbarActions();
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
                await ClearRecoveredRelaunchFailureAsync();
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

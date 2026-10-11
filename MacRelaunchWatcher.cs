using System.Diagnostics;
using System.Text.Json;

namespace ClaudexYourself;

internal static class MacRelaunchWatcher
{
    private static readonly string StatusPath = Path.Combine(Program.StateDirectory, "mac-relaunch-status.json");

    internal static void Start(MacCodexApp app)
    {
        using var running = FindRunning(app);
        if (running is null) throw new InvalidOperationException("Cannot watch macOS updates: the launched Codex process was not found.");
        using var worker = BackgroundWorker.Start("mac-relaunch-watch", app.BundlePath, app.Version, running.Id.ToString());
    }

    internal static async Task<int> RunAsync(string[] arguments)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("The macOS relaunch watcher is macOS-only.");
        if (arguments.Length != 3 || !Path.IsPathFullyQualified(arguments[0]) || !int.TryParse(arguments[2], out var pid) || pid <= 0)
            throw new ArgumentException("mac-relaunch-watch requires an app bundle, original build, and process ID.");
        Directory.CreateDirectory(Program.StateDirectory);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(Program.StateDirectory, "mac-relaunch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 0; }
        using var workerLease = lease;
        Process? running = null;
        try
        {
            var app = MacCodexApp.ReadBundle(arguments[0]) ?? throw new InvalidOperationException("The watched bundle is not Codex.");
            app = app with { Version = arguments[1] };
            running = Process.GetProcessById(pid);
            if (running.MainModule?.FileName != app.ExecutablePath) throw new InvalidOperationException("The watched process does not match the Codex bundle.");
            while (true)
            {
                await SaveStatusAsync("watching", $"Watching Codex build {app.Version} for an updater relaunch.");
                await running.WaitForExitAsync();
                running.Dispose();
                running = null;
                await SaveStatusAsync("waiting", "Codex exited; waiting up to two minutes for the updater to reopen it.");
                var replacement = await WaitForReplacementAsync(app);
                if (replacement is null)
                {
                    await SaveStatusAsync("stopped", "Codex stayed closed. No relaunch requested.");
                    return 0;
                }
                var (updatedApp, process) = replacement.Value;
                running = process;
                if (!IsUpdatedBuild(app.Version, updatedApp.Version))
                {
                    // Same-build controlled restarts may be initiated by Codex itself.
                    if (!await IsRendererReadyAsync())
                    {
                        await SaveStatusAsync("stopped", "Codex reopened without a newer build. No relaunch requested.");
                        return 0;
                    }
                }
                else
                {
                    await SaveStatusAsync("restoring", $"Restoring controlled launch after update to build {updatedApp.Version}.");
                    await RestoreAsync(IsRendererReadyAsync,
                        () => RequestQuitAsync(updatedApp, process),
                        () => process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)),
                        () =>
                        {
                            Program.LaunchMacOS(updatedApp);
                            return Task.CompletedTask;
                        });
                    running.Dispose();
                    running = FindRunning(updatedApp) ?? throw new InvalidOperationException("The restored Codex process was not found.");
                }
                app = updatedApp;
                Program.StartAutoloadWorker();
            }
        }
        catch (Exception exception)
        {
            await SaveStatusAsync("failed", $"{exception.Message} Close Codex completely and reopen it through Claudex to restore controlled mode.");
            return 1;
        }
        finally { running?.Dispose(); }
    }

    internal static bool IsUpdatedBuild(string original, string replacement) =>
        Version.TryParse(NormalizeVersion(original), out var before) &&
        Version.TryParse(NormalizeVersion(replacement), out var after) && after > before;

    private static string NormalizeVersion(string value) => value.Contains('.') ? value : value + ".0";

    // Keep quit, exit, and launch ordered: cancellation or a timeout must never launch another app.
    internal static async Task RestoreAsync(Func<Task<bool>> isReady, Func<Task> requestQuit, Func<Task> waitForExit, Func<Task> launch)
    {
        if (await isReady()) return;
        await requestQuit();
        await waitForExit();
        await launch();
    }

    private static async Task<bool> IsRendererReadyAsync()
    {
        try
        {
            await RendererDevTools.WaitForReadyAsync(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (TimeoutException) { return false; }
    }

    private static async Task<(MacCodexApp App, Process Process)?> WaitForReplacementAsync(MacCodexApp original)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromMinutes(2))
        {
            try
            {
                // Sparkle may rename Codex.app to ChatGPT.app during a branding update.
                var app = MacCodexApp.ReadBundle(original.BundlePath) ?? MacCodexApp.Find();
                var process = FindRunning(app);
                if (process is not null) return (app, process);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                // Bundle replacement is not atomic from a reader's perspective.
            }
            await Task.Delay(1000);
        }
        return null;
    }

    private static Process? FindRunning(MacCodexApp app)
    {
        Process? match = null;
        foreach (var process in Process.GetProcessesByName(Path.GetFileName(app.ExecutablePath)))
        {
            try
            {
                if (match is null && !process.HasExited && process.MainModule?.FileName == app.ExecutablePath)
                { match = process; continue; }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        return match;
    }

    private static async Task RequestQuitAsync(MacCodexApp app, Process process)
    {
        if (process.HasExited) throw new InvalidOperationException("The updated Codex app closed before controlled mode could be restored.");
        // NSRunningApplication targets this PID and requests a normal termination, including
        // the app's quit confirmation. It does not launch a closed app or force-kill it.
        const string script = """
            ObjC.import('AppKit');
            function run(args) {
                const app = $.NSRunningApplication.runningApplicationWithProcessIdentifier(Number(args[0]));
                if (app.isNil() || ObjC.unwrap(app.bundleIdentifier) !== 'com.openai.codex' ||
                    ObjC.unwrap(app.bundleURL.path) !== args[1]) throw new Error('The updated Codex process no longer matches.');
                if (!app.terminate) throw new Error('Codex declined the normal quit request.');
            }
            """;
        var start = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-l", "JavaScript", "-e", script, process.Id.ToString(), app.BundlePath }
        };
        using var request = Process.Start(start) ?? throw new InvalidOperationException("Could not request a normal Codex quit.");
        var error = request.StandardError.ReadToEndAsync();
        try { await request.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (TimeoutException)
        {
            if (!request.HasExited) request.Kill();
            throw new TimeoutException("The normal Codex quit request timed out; no controlled relaunch was attempted.");
        }
        if (request.ExitCode != 0) throw new InvalidOperationException($"Could not request a normal Codex quit: {await error}");
    }

    private static async Task SaveStatusAsync(string state, string message)
    {
        var temporary = StatusPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new WatcherStatus(state, message, Environment.ProcessId, DateTime.UtcNow), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, StatusPath, overwrite: true);
    }

    internal static async Task<string> DescribeStatusAsync()
    {
        if (!File.Exists(StatusPath)) return "macOS update relaunch: not watching (launch through Claudex to enable).";
        var status = JsonSerializer.Deserialize<WatcherStatus>(await File.ReadAllTextAsync(StatusPath))!;
        if (status.State is "watching" or "waiting" or "restoring")
        {
            try
            {
                using var worker = Process.GetProcessById(status.WorkerPid);
                if (worker.HasExited || worker.StartTime.ToUniversalTime() > status.UpdatedAtUtc)
                    return "macOS update relaunch: watcher stopped unexpectedly. Launch through Claudex to re-enable it.";
            }
            catch (ArgumentException)
            {
                return "macOS update relaunch: watcher stopped unexpectedly. Launch through Claudex to re-enable it.";
            }
        }
        return $"macOS update relaunch: {status.State}. {status.Message}";
    }

    private sealed record WatcherStatus(string State, string Message, int WorkerPid, DateTime UpdatedAtUtc);
}

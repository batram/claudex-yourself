using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace ClaudexYourself;

internal static class LaunchReliabilityChecks
{
    internal static async Task RunAsync()
    {
        const string targets = "[{\"type\":\"page\",\"url\":\"app://-/index.html\"}]";
        foreach (var healthyHost in new[] { "127.0.0.1", "[::1]" })
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var client = new HttpClient(new ProbeHandler(async (request, token) =>
            {
                if (request.RequestUri!.Host == healthyHost)
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(targets) };
                try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
                catch (OperationCanceledException) { cancelled.TrySetResult(); throw; }
                throw new Exception("Unreachable");
            }));
            var result = await DevToolsLoopback.ReadMainTargetListAsync(client).WaitAsync(TimeSpan.FromSeconds(1));
            Assert(result == targets, "healthy loopback must bypass a stalled address");
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
        using (var client = new HttpClient(new ProbeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new Exception("Unreachable");
        })))
        using (var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(40)))
        {
            try
            {
                await DevToolsLoopback.ReadMainTargetListAsync(client, deadline.Token);
                throw new Exception("Discovery ignored its deadline.");
            }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        }
        var timer = Stopwatch.StartNew();
        try
        {
            await RendererDevTools.WaitForReadyAsync(() => new TaskCompletionSource<bool>().Task,
                TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(10));
            throw new Exception("Stalled readiness probe ignored its deadline.");
        }
        catch (TimeoutException) { }
        Assert(timer.Elapsed < TimeSpan.FromSeconds(1), "probe and polling delay must fit inside the readiness deadline");

        if (OperatingSystem.IsWindows())
        {
            // Repeated WMI queries in one process previously hit cached dynamic
            // property IDs and failed with E_FAIL on the second invocation.
            for (var query = 0; query < 3; query++)
                Assert(Enum.IsDefined(WindowsCodexDiscovery.ReadLaunchMode()), "repeated WMI property reads must succeed");
            var older = "OpenAI.Codex_26.999.1.0_x64__2p2nqsd0c76g0";
            var newer = "OpenAI.Codex_26.1001.1.0_x64__2p2nqsd0c76g0";
            Assert(WindowsCodexDiscovery.SelectPackage([older, newer, "OpenAI.Codex_99.0.0.0_neutral_split.language-en_2p2nqsd0c76g0"]) == newer,
                "select application packages by numeric version, excluding resources");
            var directory = Path.Combine(Path.GetTempPath(), "claudex-launch-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (WindowsLaunchOperation.Enter(directory))
                {
                    await Task.Yield();
                    using var nested = WindowsLaunchOperation.Enter(directory, TimeSpan.Zero);
                    Task competitor;
                    using (ExecutionContext.SuppressFlow())
                        competitor = Task.Run(() =>
                        {
                            try
                            {
                                using var competingLease = WindowsLaunchOperation.Enter(directory, TimeSpan.FromMilliseconds(50));
                                throw new Exception("Concurrent launch entered a held operation.");
                            }
                            catch (TimeoutException) { }
                        });
                    await competitor;
                }
                using var released = WindowsLaunchOperation.Enter(directory, TimeSpan.Zero);
            }
            finally
            {
                File.Delete(Path.Combine(directory, "taskbar-operation.lock"));
                Directory.Delete(directory);
            }
            await CheckDetachedWorkerAsync();
        }
        Console.WriteLine("Launch reliability checks passed: loopback races, cancellation, probe deadlines, registration selection, launch locking, and detached worker isolation.");
    }

    internal static int WriteDetachedProbe(string token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!Guid.TryParseExact(token, "N", out _)) throw new ArgumentException("Invalid detached test token.");
        var path = ProbePath(token);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { pid = Environment.ProcessId, inJob = BackgroundWorker.IsInWindowsJob() }));
        File.Move(path + ".tmp", path);
        return 0;
    }

    private static async Task CheckDetachedWorkerAsync()
    {
        var token = Guid.NewGuid().ToString("N");
        var path = ProbePath(token);
        try
        {
            var pid = ControllerReload.StartDetachedWindows("self-test", "--detached-probe", token);
            var timer = Stopwatch.StartNew();
            while (!File.Exists(path) && timer.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(50);
            using var result = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert(result.RootElement.GetProperty("pid").GetInt32() == pid && !result.RootElement.GetProperty("inJob").GetBoolean(),
                "the independent worker must run outside Codex's process job");
        }
        finally { File.Delete(path); }
    }

    private static string ProbePath(string token) => Path.Combine(Path.GetTempPath(), "claudex-detach-check-" + token + ".json");
    private static void Assert(bool value, string message) { if (!value) throw new Exception("Launch check failed: " + message); }

    private sealed class ProbeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class SourceScheduleChecks
{
    internal static async Task RunAsync()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception("Schedule check failed: " + message); }
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var startup = ScriptSources.CheckSchedule.Default;
        var hourly = new ScriptSources.CheckSchedule(true, 60);
        var manual = new ScriptSources.CheckSchedule(false, 0);
        Assert(SourceCheckScheduler.IsDue(startup, start, start, null), "default checks on launch");
        Assert(!SourceCheckScheduler.IsDue(startup, start, start.AddDays(1), start), "startup-only does not repeat");
        Assert(SourceCheckScheduler.IsDue(startup, start, start, start.AddDays(-1)), "prior launch cache does not suppress startup");
        Assert(!SourceCheckScheduler.IsDue(manual, start, start.AddYears(1), null), "manual never schedules");
        Assert(!SourceCheckScheduler.IsDue(hourly, start, start.AddMinutes(59), start), "interval not early");
        Assert(SourceCheckScheduler.IsDue(hourly, start, start.AddHours(1), start), "interval deadline");
        Assert(!SourceCheckScheduler.IsDue(hourly, start, start.AddHours(1), start.AddMinutes(30)), "manual check resets interval");
        Assert(!SourceCheckScheduler.IsDue(new(false, 60), start, start, null), "interval-only waits before first check");
        Assert(SourceCheckScheduler.IsDue(new(false, 60), start, start.AddHours(1), null), "interval-only first deadline");
        foreach (var minutes in new[] { -1, 1, 14, 10081 })
        {
            try { new ScriptSources.CheckSchedule(true, minutes).Validate(); throw new Exception("Invalid interval accepted"); }
            catch (ArgumentException) { }
        }
        var root = Path.Combine(Path.GetTempPath(), "claudex-schedule-check-" + Guid.NewGuid().ToString("N"));
        var calls = 0;
        var fail = false;
        using var client = new HttpClient(new Handler(_ =>
        {
            Interlocked.Increment(ref calls);
            return fail ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content = new StringContent("{\"version\":\"9.0.0\",\"downloadUrl\":\"https://example.test/release\"}", Encoding.UTF8) };
        }));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "scripts"));
            await File.WriteAllTextAsync(Path.Combine(root,"scripts","test.js"), "// ==ClaudexUserScript==\n// @name Test\n// @id test\n// @version 1.0.0\n// @description Test\n// @run-at renderer-ready\n// @platform windows, macos, linux\n// @grant none\n// ==/ClaudexUserScript==\nreturn true;");
            var service = new ScriptSources(root, client, () => "build");
            Assert(service.DefaultSchedule == startup, "old configuration defaults to startup");
            await service.SetSourceAsync(null, "https://example.test/release.json");
            await service.SetScheduleAsync(null, true, 60);
            await service.SetScheduleAsync("test", false, 0);
            var snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("defaultSchedule").GetProperty("intervalMinutes").GetInt32() == 60, "global schedule saved");
            Assert(!snapshot.GetProperty("scripts")[0].GetProperty("scheduleInherited").GetBoolean(), "script override saved");
            await service.SetScheduleAsync("test", true, 0, inherit:true);
            snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("scripts")[0].GetProperty("scheduleInherited").GetBoolean() && snapshot.GetProperty("scripts")[0].GetProperty("schedule").GetProperty("intervalMinutes").GetInt32() == 60, "inherit resolves current default");
            await service.SetScheduleAsync(null, true, 0);
            var scheduler = new SourceCheckScheduler(service);
            scheduler.Tick("launch-a", DateTime.UtcNow);
            await scheduler.DrainAsync();
            Assert(calls == 1, "startup checks without a panel or renderer operation");
            scheduler.Tick("launch-a", DateTime.UtcNow.AddSeconds(1));
            await scheduler.DrainAsync();
            Assert(calls == 1, "same launch deduplicates automatic checks");
            snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("claudexCheck").GetProperty("result").GetProperty("updateAvailable").GetBoolean(), "background result appears in snapshot");
            fail = true;
            scheduler.Tick("launch-b", DateTime.UtcNow.AddSeconds(2));
            await scheduler.DrainAsync();
            Assert(calls == 2, "new launch checks again");
            scheduler.Tick("launch-b", DateTime.UtcNow.AddSeconds(3));
            await scheduler.DrainAsync();
            Assert(calls == 2, "failed startup check does not hammer the source");
            snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("claudexCheck").GetProperty("error").GetString()!.Contains("503"), "failures visible and persisted");
            await service.SetScheduleAsync(null, false, 0);
            scheduler.Tick("launch-c", DateTime.UtcNow.AddHours(1));
            await scheduler.DrainAsync();
            Assert(calls == 2, "manual disables startup checks");
            await service.SetSourceAsync(null,"https://example.test/other.json");
            snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("claudexCheck").ValueKind == JsonValueKind.Null,"source changes invalidate old results");
            // A slow check does not block Tick or create duplicates, and at most three run at once.
            for (var i = 0; i < 4; i++)
            {
                var source = (await File.ReadAllTextAsync(Path.Combine(root,"scripts","test.js"))).Replace("@id test", "@id test" + i);
                await File.WriteAllTextAsync(Path.Combine(root,"scripts","test" + i + ".js"),source);
                await service.SetSourceAsync("test" + i,"https://example.test/test" + i + ".js");
            }
            await service.SetScheduleAsync(null,true,0);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started = 0;
            var slow = new SourceCheckScheduler(service, _ => { Interlocked.Increment(ref started); return gate.Task; });
            slow.Tick("slow",DateTime.UtcNow.AddDays(1));
            slow.Tick("slow",DateTime.UtcNow.AddDays(1));
            Assert(started == 3,"bounded concurrency and in-flight deduplication");
            gate.SetResult(); await slow.DrainAsync();
            Console.WriteLine("Source schedule checks passed: defaults, inheritance, intervals, launch detection, manual mode, caching, failure backoff, and concurrency.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private sealed class Handler(Func<HttpRequestMessage,HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}

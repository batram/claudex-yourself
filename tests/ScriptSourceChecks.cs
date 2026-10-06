using System.Net;
using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class ScriptSourceChecks
{
    internal static async Task RunAsync()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception("Source check failed: " + message); }
        static async Task Reject(Func<Task> action, string message)
        {
            try { await action(); } catch (Exception error) when (error is not TestFailure) { return; }
            throw new TestFailure("Expected rejection: " + message);
        }
        static string Source(string version, string id = "demo", string platforms = "windows, macos, linux") =>
            $"// ==ClaudexUserScript==\n// @name Demo\n// @id {id}\n// @version {version}\n// @description Test\n// @run-at renderer-ready\n// @platform {platforms}\n// @grant none\n// ==/ClaudexUserScript==\nreturn true;";
        var root = Path.Combine(Path.GetTempPath(), "claudex-source-check-" + Guid.NewGuid().ToString("N"));
        var contents = Source("1.0.0");
        using var client = new HttpClient(new Handler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/missing") return new(HttpStatusCode.NotFound);
            if (path == "/redirect") return new(HttpStatusCode.Redirect) { Headers = { Location = new Uri("http://example.test/script.js") } };
            return new(HttpStatusCode.OK) { Content = new StringContent(path == "/release.json" ? "{\"version\":\"9.0.0\",\"downloadUrl\":\"https://example.test/release\"}" : contents, Encoding.UTF8) };
        }));
        var service = new ScriptSources(root, client, () => "test-build");
        try
        {
            Assert(ScriptSources.NormalizeUrl("https://github.com/owner/repo/blob/main/scripts/demo.js").AbsoluteUri == "https://raw.githubusercontent.com/owner/repo/main/scripts/demo.js", "GitHub file normalization");
            foreach (var bad in new[] { "file:///tmp/test", "javascript:alert(1)", "https://user:pass@example.test/a", "https://example.test/a#b" })
                await Reject(() => Task.FromResult(ScriptSources.NormalizeUrl(bad)), "unsafe URL " + bad);
            foreach (var (a, b, expected) in new[] { ("1.10.0", "1.9.0", 1), ("1.0.0", "1.0", 0), ("v2.0.0", "1.9.9", 1), ("1.0.0-beta.2", "1.0.0-beta.10", -1), ("1.0.0", "1.0.0-rc.1", 1), ("1.0.0+a", "1.0.0+b", 0), ("1.0.0-1", "1.0.0-alpha", -1) })
                Assert(Math.Sign(ScriptSources.CompareVersions(a, b)) == expected, "version order " + a);
            await Reject(() => Task.FromResult(ScriptSources.CompareVersions("oops", "1.0")), "invalid version");
            await Reject(() => service.DownloadAsync("https://example.test/redirect"), "HTTPS downgrade redirect");
            await Reject(() => service.DownloadAsync("https://example.test/missing"), "HTTP failure");
            var preview = await service.PreviewAsync("https://example.test/script.js");
            Assert(!Directory.Exists(Path.Combine(root, "scripts")), "preview never publishes or executes");
            Assert(preview.Metadata.Id == "demo" && !preview.Compatibility.Tested, "metadata and exact-build warning");
            contents = Source("9.0.0");
            var activated = false;
            await service.InstallAsync(preview.PreviewId, true, true, (source, id) =>
            {
                Assert(source == Source("1.0.0") && id == "demo", "activate exactly the reviewed source");
                Assert(!File.Exists(Path.Combine(root, "autoload.json")), "activate before exposing a new script to autoload");
                activated = true;
                return Task.FromResult<object>(new[] { new { activated = true } });
            });
            Assert(activated, "explicit run activates the installed script");
            var scriptPath = Path.Combine(root, "scripts", "demo.js");
            Assert(File.ReadAllText(scriptPath) == Source("1.0.0"), "install uses reviewed bytes even if remote changes");
            Assert(File.ReadAllText(Path.Combine(root, "sources.json")).Contains("https://example.test/script.js"), "URL retained");
            Assert(File.ReadAllText(Path.Combine(root, "autoload.json")).Contains("demo"), "autoload enabled");
            await Reject(() => service.InstallAsync(preview.PreviewId, true, false), "preview cannot be replayed");
            var check = JsonSerializer.SerializeToElement(await service.CheckScriptAsync("demo"), ScriptSources.Json);
            Assert(check.GetProperty("updateAvailable").GetBoolean(), "new remote version detected");
            foreach (var version in new[] { "1.0.0", "0.9.0" })
            {
                contents = Source(version);
                await Reject(() => service.PreviewAsync("https://example.test/script.js", "demo"), "equal version or downgrade");
            }
            contents = Source("2.0.0", "different");
            await Reject(() => service.CheckScriptAsync("demo"), "mismatching identity in check");
            await Reject(() => service.PreviewAsync("https://example.test/script.js", "demo"), "mismatching identity in preview");
            contents = Source("2.0.0");
            preview = await service.PreviewAsync("https://example.test/script.js", "demo");
            File.AppendAllText(scriptPath, "\n// local edit");
            await Reject(() => service.InstallAsync(preview.PreviewId, true, false), "local changes since preview");
            preview = await service.PreviewAsync("https://example.test/script.js", "demo");
            var old = File.ReadAllText(scriptPath);
            var failedActivation = JsonSerializer.SerializeToElement(await service.InstallAsync(preview.PreviewId, false, true, (_, _) => throw new IOException("Renderer unavailable")), ScriptSources.Json);
            Assert(failedActivation.GetProperty("installed").GetBoolean() && failedActivation.GetProperty("activationError").GetString() == "Renderer unavailable", "publication and activation failures distinct");
            Assert(File.ReadAllText(Path.Combine(root, "source-backups", "demo.js")) == old, "local edits preserved in backup");
            Assert(!File.ReadAllText(Path.Combine(root, "autoload.json")).Contains("demo"), "autoload disabled explicitly");
            contents = Source("3.0.0", platforms:"unsupported");
            preview = await service.PreviewAsync("https://example.test/script.js", "demo");
            await Reject(() => service.InstallAsync(preview.PreviewId, false, false), "unsupported platform");
            contents = Source("3.0.0");
            preview = await service.PreviewAsync("https://example.test/script.js", "demo");
            var previewPath = Path.Combine(root, "source-previews", preview.PreviewId + ".json");
            await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(preview with { ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1) }, ScriptSources.Json));
            await Reject(() => service.InstallAsync(preview.PreviewId, false, false), "expired review");
            preview = await service.PreviewAsync("https://example.test/script.js", "demo");
            previewPath = Path.Combine(root, "source-previews", preview.PreviewId + ".json");
            await File.WriteAllTextAsync(previewPath, JsonSerializer.Serialize(preview with { Source = preview.Source + "\n// changed after review" }, ScriptSources.Json));
            await Reject(() => service.InstallAsync(preview.PreviewId, false, false), "tampered preview bytes");
            contents = "<html>not a userscript</html>";
            await Reject(() => service.PreviewAsync("https://example.test/script.js"), "HTML response");
            contents = new string('x', 1024 * 1024 + 1);
            await Reject(() => service.PreviewAsync("https://example.test/script.js"), "size limit");
            await service.SetSourceAsync(null, "https://example.test/release.json");
            var release = JsonSerializer.SerializeToElement(await service.CheckClaudexAsync(), ScriptSources.Json);
            Assert(release.GetProperty("updateAvailable").GetBoolean(), "global manifest check");
            await Reject(() => service.SetSourceAsync("missing", "https://example.test/script.js"), "configure only installed scripts");
            var snapshot = JsonSerializer.SerializeToElement(service.Snapshot(), ScriptSources.Json);
            Assert(snapshot.GetProperty("scripts").GetArrayLength() == 1, "installed inventory");
            await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Task.Run(() => AutoloadScripts.SetInDirectory(root, "parallel_" + i, true))));
            Assert(JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(root, "autoload.json")))!.Length == 8, "concurrent autoload writes preserve every entry");
            Console.WriteLine("Source checks passed: download bounds, URLs, versions, preview/install, races, backups, compatibility, autoload, and global release checks.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class TestFailure(string message) : Exception(message);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}

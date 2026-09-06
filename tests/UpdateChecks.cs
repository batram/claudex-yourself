using System.IO.Compression;
using System.Security;

namespace ClaudexYourself;

internal static class UpdateChecks
{
    public static async Task RunAsync()
    {
        var installed = new CodexUpdates.Package("OpenAI.Codex_26.901.4073.0_x64__2p2nqsd0c76g0", "OpenAI.Codex_2p2nqsd0c76g0", "26.901.4073.0", "CN=Test", "x64", "Ok");
        var release = new CodexUpdates.Release("26.901.5280.0", "OpenAI.Codex", "9PLM9XGG6VKS", 1);
        var check = CodexUpdates.ValidateRelease(installed, release);
        Assert(check.UpdateAvailable && check.PackageUrl == "https://persistent.oaistatic.com/codex-app-prod/ChatGPT-x64.msix", "new update");
        Assert(!CodexUpdates.ValidateRelease(installed, release with { BuildVersion = installed.Version }).UpdateAvailable, "equal version");
        Assert(!CodexUpdates.ValidateRelease(installed, release with { BuildVersion = "26.800.1.0" }).UpdateAvailable, "no downgrade");
        Reject(() => CodexUpdates.ValidateRelease(installed, release with { PackageIdentity = "Other.App" }));
        Reject(() => CodexUpdates.ValidateRelease(installed, release with { BuildVersion = "26.901.5280.0/../../other" }));
        Reject(() => CodexUpdates.ValidateRelease(installed, release with { StoreProductId = "other" }));
        Reject(() => CodexUpdates.ValidateRelease(installed, release with { SchemaVersion = 2 }));
        Assert(!CodexUpdates.IsPackageInUse("0x80070005 Access denied"), "do not retry arbitrary access denied");
        Assert(CodexUpdates.IsPackageInUse("0x80073D02"), "retry package-in-use");
        await CheckInstallRecoveryAsync();
        await CheckDownloadRetriesAsync(check);
        await CheckAvailableVersionsAsync(check);
        var path = Path.Combine(Path.GetTempPath(), $"claudex-update-test-{Guid.NewGuid():N}.msix");
        try
        {
            WritePackage(path, check.AvailableVersion, installed.Publisher);
            CodexUpdates.ValidatePackage(path, check);
            File.Delete(path);
            WritePackage(path, "26.901.9999.0", installed.Publisher);
            Reject(() => CodexUpdates.ValidatePackage(path, check));
            File.Delete(path);
            WritePackage(path, check.AvailableVersion, "CN=Untrusted");
            Reject(() => CodexUpdates.ValidatePackage(path, check));
        }
        finally { File.Delete(path); }
        var stagedRoot = Path.Combine(Path.GetTempPath(), $"claudex-staged-test-{Guid.NewGuid():N}");
        var stagedDirectory = Path.Combine(stagedRoot, "OpenAI.Codex_26.901.5280.0_x64__2p2nqsd0c76g0");
        var stagedPath = Path.Combine(stagedDirectory, "AppxManifest.xml");
        try
        {
            var stagedCheck = check with { Installed = installed with { InstallLocation = Path.Combine(stagedRoot, installed.FullName) } };
            Assert(CodexUpdates.FindStagedManifest(stagedCheck) is null, "missing staged package");
            Directory.CreateDirectory(stagedDirectory);
            await File.WriteAllTextAsync(stagedPath, "<Package><Identity Name=\"OpenAI.Codex\" Publisher=\"CN=Test\" Version=\"26.901.5280.0\" ProcessorArchitecture=\"x64\" /></Package>");
            Assert(CodexUpdates.FindStagedManifest(stagedCheck) == stagedPath, "exact staged package");
            await File.WriteAllTextAsync(stagedPath, "<Package><Identity Name=\"Other.App\" Publisher=\"CN=Test\" Version=\"26.901.5280.0\" ProcessorArchitecture=\"x64\" /></Package>");
            Reject(() => CodexUpdates.FindStagedManifest(stagedCheck));
        }
        finally
        {
            if (File.Exists(stagedPath)) File.Delete(stagedPath);
            if (Directory.Exists(stagedDirectory)) Directory.Delete(stagedDirectory);
            if (Directory.Exists(stagedRoot)) Directory.Delete(stagedRoot);
        }
        // Exercise the shutdown race: absence followed by another live process resets the quiet period.
        var samples = 0;
        await CodexUpdates.WaitForQuietAsync(() => ++samples is 1 or 4, TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10));
        Assert(samples >= 8, "quiet period resets when a package process reappears");
        try
        {
            await CodexUpdates.WaitForQuietAsync(() => true, TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(5));
            throw new Exception("A live process must prevent installation.");
        }
        catch (TimeoutException) { }
        // Activation alone must never count as a completed controlled restart.
        var probes = 0;
        await RendererDevTools.WaitForReadyAsync(() => Task.FromResult(++probes == 4), TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));
        Assert(probes == 4, "wait for the renderer after activation");
        try
        {
            await RendererDevTools.WaitForReadyAsync(() => Task.FromResult(false), TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(5));
            throw new Exception("An uncontrolled launch must not report update success.");
        }
        catch (TimeoutException) { }
    }

    private static async Task CheckInstallRecoveryAsync()
    {
        var modes = new List<bool>();
        var retries = new List<int>();
        await CodexUpdates.RetryPackageInstallAsync(finish =>
        {
            modes.Add(finish);
            return finish ? Task.CompletedTask : Task.FromException(new InvalidOperationException("0x80073D02"));
        }, attempt => { retries.Add(attempt); return Task.CompletedTask; });
        Assert(modes.SequenceEqual(new[] { false, true }) && retries.SequenceEqual(new[] { 1 }), "package-in-use enables target shutdown only on retry");
        modes.Clear(); retries.Clear();
        await CodexUpdates.RetryPackageInstallAsync(finish => { modes.Add(finish); return Task.CompletedTask; }, attempt => { retries.Add(attempt); return Task.CompletedTask; });
        Assert(modes.SequenceEqual(new[] { false }) && retries.Count == 0, "successful ordinary install needs no shutdown recovery");
        foreach (var message in new[] { "0x80070005", "0x80073D02" })
        {
            modes.Clear(); retries.Clear();
            try
            {
                await CodexUpdates.RetryPackageInstallAsync(finish => { modes.Add(finish); return Task.FromException(new InvalidOperationException(message)); }, attempt => { retries.Add(attempt); return Task.CompletedTask; });
                throw new Exception("Installation failure must remain visible.");
            }
            catch (InvalidOperationException exception) when (exception.Message == message) { }
            Assert(modes.Count == (message == "0x80073D02" ? 3 : 1), "bounded retries only for package-in-use");
        }
        var calls = 0;
        try
        {
            await CodexUpdates.RetryPackageInstallAsync(finish =>
            {
                calls++;
                return Task.FromException(finish ? new TimeoutException("Codex did not exit") : new InvalidOperationException("0x80073D02"));
            }, _ => Task.CompletedTask);
            throw new Exception("Failed exit check must stop recovery.");
        }
        catch (TimeoutException) { }
        Assert(calls == 2, "quit timeout stops recovery without another retry");
    }

    private static async Task CheckDownloadRetriesAsync(CodexUpdates.UpdateCheck check)
    {
        var path = Path.Combine(Path.GetTempPath(), $"claudex-download-test-{Guid.NewGuid():N}.msix");
        string? sourceVersion = "26.901.4073.0";
        string? sourceIdentity = "OpenAI.Codex";
        var etag = "\"old\"";
        var requests = 0;
        using var client = new HttpClient(new HeaderHandler(request =>
        {
            Assert(request.Method == HttpMethod.Head, "retry checks headers without downloading installer bytes");
            requests++;
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            if (sourceVersion is not null) response.Headers.Add("x-ms-meta-package_version", sourceVersion);
            if (sourceIdentity is not null) response.Headers.Add("x-ms-meta-package_identity", sourceIdentity);
            response.Headers.ETag = new(etag);
            return response;
        }));
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
                Assert((await CodexUpdates.GetDownloadBlockAsync(client, check, path))?.Contains(sourceVersion) == true, "announced version ahead of download stays blocked");
            Assert(requests == 3, "retries use only three HEAD requests");
            sourceVersion = check.AvailableVersion;
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is null, "newly published target enables download");
            sourceIdentity = "Other.App";
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is not null, "wrong source identity blocks download");
            sourceIdentity = "OpenAI.Codex";
            WritePackage(path, "26.901.4073.0", check.Installed.Publisher);
            sourceVersion = null;
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is not null, "legacy rejected cache needs evidence of replacement");
            var file = new FileInfo(path);
            var receipt = new CodexUpdates.DownloadReceipt(check.PackageUrl, etag, file.Length, file.LastWriteTimeUtc);
            await File.WriteAllTextAsync(path + ".http.json", System.Text.Json.JsonSerializer.Serialize(receipt));
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is not null, "unchanged rejected ETag prevents repeat download");
            etag = "\"new\"";
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is null, "changed ETag permits a new candidate for validation");
            await File.WriteAllTextAsync(path + ".http.json", System.Text.Json.JsonSerializer.Serialize(receipt with { Length = file.Length + 1 }));
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is not null, "unrelated receipt cannot authorize retry");
            File.Delete(path);
            WritePackage(path, check.AvailableVersion, check.Installed.Publisher);
            var before = requests;
            Assert(await CodexUpdates.GetDownloadBlockAsync(client, check, path) is null && requests == before, "valid cached package needs no remote download check");
            using var staleGet = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            staleGet.Headers.Add("x-ms-meta-package_version", "26.901.4073.0");
            Assert(CodexUpdates.DownloadHeaderBlock(staleGet, check) is not null, "GET headers reject source regression after HEAD");
        }
        finally { File.Delete(path); File.Delete(path + ".http.json"); }
    }

    private static async Task CheckAvailableVersionsAsync(CodexUpdates.UpdateCheck seed)
    {
        var announced = seed with { AvailableVersion = "26.901.6511.0" };
        var stableVersion = "26.901.5280.0";
        var versionedAvailable = false;
        var versionedFails = false;
        var stableIdentity = "OpenAI.Codex";
        using var client = new HttpClient(new HeaderHandler(request =>
        {
            Assert(request.Method == HttpMethod.Head, "availability discovery never downloads packages");
            var versioned = request.RequestUri!.AbsolutePath.Contains("/releases/");
            if (versioned && versionedFails) return new(System.Net.HttpStatusCode.ServiceUnavailable);
            if (versioned && !versionedAvailable) return new(System.Net.HttpStatusCode.NotFound);
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Headers.Add("x-ms-meta-package_version", versioned ? announced.AvailableVersion : stableVersion);
            response.Headers.Add("x-ms-meta-package_identity", versioned ? "OpenAI.Codex" : stableIdentity);
            response.Headers.Add("x-ms-meta-architecture", "x64");
            return response;
        }));
        var available = await CodexUpdates.ResolveAvailableAsync(client, announced, []);
        Assert(available.UpdateAvailable && available.AvailableVersion == stableVersion && available.AnnouncedVersion == announced.AvailableVersion, "install an obtainable intermediate release without waiting for the announcement");
        var current = await CodexUpdates.ResolveAvailableAsync(client, announced with { Installed = announced.Installed with { Version = stableVersion } }, []);
        Assert(!current.UpdateAvailable && current.AvailableVersion == stableVersion, "do not redownload or reinstall the current stable package");
        var ahead = await CodexUpdates.ResolveAvailableAsync(client, announced with { Installed = announced.Installed with { Version = "26.901.9999.0" } }, []);
        Assert(!ahead.UpdateAvailable && ahead.AvailableVersion == "26.901.9999.0", "installed version is the floor; never downgrade");
        versionedAvailable = true;
        available = await CodexUpdates.ResolveAvailableAsync(client, announced, []);
        Assert(available.AvailableVersion == announced.AvailableVersion && available.Source == "version-specific", "prefer obtainable version-specific release over lagging alias");
        stableVersion = "26.901.7000.0";
        available = await CodexUpdates.ResolveAvailableAsync(client, announced, []);
        Assert(available.AvailableVersion == stableVersion, "stable asset ahead of announcement is also eligible");
        versionedFails = true;
        available = await CodexUpdates.ResolveAvailableAsync(client, announced, []);
        Assert(available.UpdateAvailable && available.SourceWarning is not null, "one source failure does not hide a valid update");
        stableIdentity = "Other.App";
        available = await CodexUpdates.ResolveAvailableAsync(client, announced, []);
        Assert(!available.UpdateAvailable && available.SourceWarning is not null, "invalid candidates are rejected without claiming a successful complete check");
        versionedFails = false; versionedAvailable = false; stableIdentity = "OpenAI.Codex"; stableVersion = "26.901.5280.0";
        available = await CodexUpdates.ResolveAvailableAsync(client, announced, [new(announced.AvailableVersion, seed.PackageUrl, "windows-staged")]);
        Assert(available.Source == "windows-staged", "already staged announced update beats older downloads");
        var directory = Path.Combine(Path.GetTempPath(), $"claudex-cache-version-test-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "ChatGPT-x64-26.901.6511.0.msix");
        Directory.CreateDirectory(directory);
        try
        {
            WritePackage(path, stableVersion, announced.Installed.Publisher);
            var cached = CodexUpdates.ReadCachedCandidates(announced, directory).ToArray();
            Assert(cached.Length == 1 && cached[0].Version == stableVersion, "cache version comes from its manifest, not the filename");
            available = await CodexUpdates.ResolveAvailableAsync(client, announced, cached);
            Assert(available.CachedPackagePath == path && available.AvailableVersion == stableVersion, "reuse a correctly identified cache under the old misleading filename");
            File.Delete(path);
            WritePackage(path, "26.901.9999.0", "CN=Untrusted");
            Assert(!CodexUpdates.ReadCachedCandidates(announced, directory).Any(), "untrusted cached package cannot become a candidate");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private sealed class HeaderHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }

    private static void WritePackage(string path, string version, string publisher)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("AppxManifest.xml").Open());
        writer.Write($"<Package><Identity Name=\"OpenAI.Codex\" Publisher=\"{SecurityElement.Escape(publisher)}\" Version=\"{version}\" ProcessorArchitecture=\"x64\" /></Package>");
    }
    private static void Assert(bool condition, string test) { if (!condition) throw new Exception($"Update check failed: {test}"); }
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Unsafe update metadata was accepted.");
    }
}

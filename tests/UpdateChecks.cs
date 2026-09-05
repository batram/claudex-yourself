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

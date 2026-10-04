using System.Diagnostics;
using System.Xml.Linq;

namespace ClaudexYourself;

internal sealed record MacCodexApp(string BundlePath, string ExecutablePath, string Version)
{
    internal static MacCodexApp Find()
    {
        foreach (var root in new[] { "/Applications", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications") })
        foreach (var name in new[] { "Codex.app", "ChatGPT.app" })
        {
            var app = ReadBundle(Path.Combine(root, name));
            if (app is not null) return app;
        }
        throw new FileNotFoundException("Codex Desktop was not found in /Applications or ~/Applications (Codex.app or ChatGPT.app with bundle ID com.openai.codex).");
    }

    internal static MacCodexApp? ReadBundle(string bundle)
    {
        var plist = Path.Combine(bundle, "Contents", "Info.plist");
        if (!File.Exists(plist)) return null;
        using var process = Process.Start(new ProcessStartInfo("/usr/bin/plutil")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-convert", "xml1", "-o", "-", plist }
        }) ?? throw new InvalidOperationException("Could not read the Codex app bundle.");
        var xml = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Could not read {plist}: {error}");
        return ParseBundle(bundle, xml);
    }

    internal static MacCodexApp? ParseBundle(string bundle, string xml)
    {
        var dictionary = XDocument.Parse(xml).Root?.Element("dict");
        string? Read(string key) => dictionary?.Elements("key")
            .FirstOrDefault(element => element.Value == key)?.ElementsAfterSelf().FirstOrDefault()?.Value;
        // The classic ChatGPT app is a different product, even when the filename matches.
        if (Read("CFBundleIdentifier") != "com.openai.codex") return null;
        var executable = Read("CFBundleExecutable");
        var version = Read("CFBundleVersion");
        if (string.IsNullOrWhiteSpace(executable) || Path.GetFileName(executable) != executable || string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"Invalid Codex bundle metadata in {bundle}.");
        var path = Path.Combine(bundle, "Contents", "MacOS", executable);
        if (!File.Exists(path)) throw new FileNotFoundException("The Codex desktop executable was not found.", path);
        return new(bundle, path, version);
    }

    internal static bool IsDesktopExecutable(string path)
    {
        var macOS = Path.GetDirectoryName(path);
        var contents = macOS is null ? null : Path.GetDirectoryName(macOS);
        var bundle = contents is null ? null : Path.GetDirectoryName(contents);
        if (bundle is null || Path.GetFileName(macOS) != "MacOS" || Path.GetFileName(contents) != "Contents"
            || !bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return false;
        return ReadBundle(bundle)?.ExecutablePath == path;
    }
}

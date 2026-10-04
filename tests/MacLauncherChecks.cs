using System.Security;

namespace ClaudexYourself;

internal static class MacLauncherChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "claudex-mac-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var name in new[] { "Codex", "ChatGPT" })
            {
                var bundle = Path.Combine(root, name + ".app");
                var executable = Path.Combine(bundle, "Contents", "MacOS", name);
                Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
                File.WriteAllText(executable, "fixture");
                string Plist(string id, string binary) => $"<plist version=\"1.0\"><dict><key>CFBundleIdentifier</key><string>{id}</string><key>CFBundleExecutable</key><string>{SecurityElement.Escape(binary)}</string><key>CFBundleVersion</key><string>12947</string></dict></plist>";
                File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"), Plist("com.openai.codex", name));
                var app = MacCodexApp.ReadBundle(bundle);
                Assert(app?.ExecutablePath == executable && app.Version == "12947", "bundle metadata for " + name);
                Assert(MacCodexApp.IsDesktopExecutable(executable), "desktop identification for " + name);
                Assert(!MacCodexApp.IsDesktopExecutable(Path.Combine(bundle, "Contents", "Resources", "codex")), "CLI is not desktop");
                Assert(MacCodexApp.ParseBundle(bundle, Plist("com.openai.chat", name)) is null, "classic ChatGPT excluded");
                try
                {
                    MacCodexApp.ParseBundle(bundle, Plist("com.openai.codex", "../elsewhere"));
                    throw new Exception("Executable traversal was accepted.");
                }
                catch (InvalidOperationException) { }
            }
            Assert(MacCodexApp.ReadBundle(Path.Combine(root, "Missing.app")) is null, "absent bundle");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Mac launcher check failed: " + message);
    }
}

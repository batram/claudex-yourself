using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class LinuxLauncherChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "claudex-linux-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "resources"));
            var executable = Path.Combine(root, "ChatGPT");
            File.WriteAllText(executable, "fixture");
            File.WriteAllText(Path.Combine(root, "resources", "codex"), "CLI fixture");
            void WriteArchive(string name)
            {
                var package = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { name, version = "26.930.61225" }));
                var header = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { files = new Dictionary<string, object> { ["package.json"] = new { size = package.Length, offset = "0" } } }));
                var padded = (header.Length + 3) / 4 * 4;
                using var writer = new BinaryWriter(File.Create(Path.Combine(root, "resources", "app.asar")));
                writer.Write(4u);
                writer.Write((uint)(padded + 8));
                writer.Write((uint)(padded + 4));
                writer.Write((uint)header.Length);
                writer.Write(header);
                writer.Write(new byte[padded - header.Length]);
                writer.Write(package);
            }
            WriteArchive("openai-codex-electron");
            var app = LinuxCodexApp.ReadExecutable(executable) ?? throw new Exception("Linux Desktop not identified.");
            if (app.Version != "26.930.61225") throw new Exception("Wrong Linux Codex version.");
            if (!app.StartInfo(9229).ArgumentList.SequenceEqual(new[] { "--remote-debugging-port=9229", "--remote-debugging-address=127.0.0.1" }))
                throw new Exception("Unexpected Linux launch arguments.");
            var link = Path.Combine(root, "desktop-link");
            File.CreateSymbolicLink(link, executable);
            if (LinuxCodexApp.ReadExecutable(link)?.ExecutablePath != executable) throw new Exception("Linux desktop symlink not resolved.");
            if (LinuxCodexApp.ReadExecutable(Path.Combine(root, "resources", "codex")) is not null) throw new Exception("CLI identified as Desktop.");
            WriteArchive("openai-chatgpt");
            if (LinuxCodexApp.ReadExecutable(executable) is not null) throw new Exception("Classic ChatGPT identified as Codex.");
        }
        finally { Directory.Delete(root, true); }
    }
}

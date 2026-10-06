using System.Diagnostics;
using System.Text.Json;

namespace ClaudexYourself;

internal sealed record LinuxCodexApp(string ExecutablePath, string Version)
{
    internal static LinuxCodexApp Find()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDEX_CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured))
            return ReadExecutable(Path.GetFullPath(configured))
                ?? throw new InvalidOperationException("CLAUDEX_CODEX_EXECUTABLE must point to a Codex Desktop executable with resources/app.asar alongside it, not the Codex CLI.");
        foreach (var path in new[] { "/usr/lib/chatgpt/ChatGPT", "/usr/lib/codex/Codex", "/opt/Codex/Codex", "/opt/codex/codex" })
            if (ReadExecutable(path) is { } app) return app;
        throw new FileNotFoundException("Codex Desktop was not found. Set CLAUDEX_CODEX_EXECUTABLE to the installed Electron executable.");
    }

    internal static LinuxCodexApp? ReadExecutable(string path)
    {
        if (!File.Exists(path)) return null;
        path = new FileInfo(path).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(path);
        var archive = Path.Combine(Path.GetDirectoryName(path)!, "resources", "app.asar");
        if (!File.Exists(archive)) return null;
        // Electron ASAR stores a Pickle-encoded JSON header followed by file data.
        // Read only package.json; Chromium's adjacent version file is Electron's version.
        using var stream = File.OpenRead(archive);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 16 || reader.ReadUInt32() != 4) return null;
        var headerSize = reader.ReadUInt32();
        reader.ReadUInt32();
        var jsonSize = reader.ReadUInt32();
        if (headerSize < 8 || headerSize > 32 * 1024 * 1024 || jsonSize > headerSize - 8 || 8L + headerSize > stream.Length)
            throw new InvalidDataException($"Invalid Electron ASAR header: {archive}");
        using var header = JsonDocument.Parse(reader.ReadBytes((int)jsonSize));
        if (!header.RootElement.GetProperty("files").TryGetProperty("package.json", out var entry)) return null;
        var size = entry.GetProperty("size").GetInt32();
        var offset = long.Parse(entry.GetProperty("offset").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        var position = 8L + headerSize + offset;
        if (size < 0 || size > 1024 * 1024 || offset < 0 || position < 0 || position > stream.Length - size)
            throw new InvalidDataException($"Invalid package.json entry: {archive}");
        stream.Position = position;
        using var package = JsonDocument.Parse(reader.ReadBytes(size));
        if (package.RootElement.GetProperty("name").GetString() != "openai-codex-electron") return null;
        var version = package.RootElement.GetProperty("version").GetString();
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidDataException("Codex package version is missing.");
        return new(path, version);
    }

    internal ProcessStartInfo StartInfo(int port)
    {
        var start = new ProcessStartInfo(ExecutablePath) { UseShellExecute = false };
        start.ArgumentList.Add($"--remote-debugging-port={port}");
        start.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
        return start;
    }
}

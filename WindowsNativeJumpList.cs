using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudexYourself;

// Read-only compatibility adapter for Windows' serialized native Shell Links.
// Windows exposes automatic Recent lists through COM, but not custom categories
// or user tasks. Preserve the native links themselves rather than recreating URLs.
internal static class WindowsNativeJumpList
{
    internal sealed record Link(byte[] Bytes, bool RecentChat, string Arguments);
    internal sealed record Snapshot(string Source, DateTime Modified, long Length, Link[] Links);
    private const int Limit = 2 * 1024 * 1024;
    private static readonly byte[] Header = Convert.FromHexString("4C0000000114020000000000C000000000000046");
    private static readonly byte[] NativeId = Encoding.Unicode.GetBytes("OpenAI.Codex_2p2nqsd0c76g0!App");
    private static readonly byte[] OwnTask = Encoding.Unicode.GetBytes("taskbar claudex");
    private static Snapshot? cached;

    internal static Snapshot? Read()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (cached is not null)
        {
            var file = new FileInfo(cached.Source);
            if (file.Exists && file.LastWriteTimeUtc == cached.Modified && file.Length == cached.Length) return cached;
            if (file.Exists) return cached = Load(file);
            cached = null;
        }
        // Resolve the user's Recent folder through Windows; no profile guessing.
        var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
        if (!Directory.Exists(recent)) return null;
        var folder = Directory.EnumerateDirectories(recent).FirstOrDefault(path => Path.GetFileName(path).Equals("CustomDestinations", StringComparison.OrdinalIgnoreCase));
        if (folder is null) return null;
        foreach (var file in Directory.EnumerateFiles(folder, "*.customDestinations-ms").Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Take(128))
        {
            try
            {
                var snapshot = Load(file);
                if (snapshot is not null) return cached = snapshot;
            }
            catch (IOException) { } // A different app may be publishing its list.
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    private static Snapshot? Load(FileInfo file)
    {
        if (file.Length is <= 0 or > Limit) return null;
        byte[] bytes;
        using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > Limit) return null;
            bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
        }
        if (bytes.AsSpan().IndexOf(NativeId) < 0 || bytes.AsSpan().IndexOf(OwnTask) >= 0) return null;
        var links = ExtractLinks(bytes);
        return new(file.FullName, file.LastWriteTimeUtc, file.Length, links);
    }

    internal static Link[] ExtractLinks(byte[] bytes)
    {
        if (bytes.Length > Limit) return [];
        var offsets = new List<int>();
        for (var position = 0; position <= bytes.Length - Header.Length; position++)
            if (bytes.AsSpan(position, Header.Length).SequenceEqual(Header)) offsets.Add(position);
        var links = new List<Link>();
        var actions = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < offsets.Count; index++)
        {
            var start = offsets[index];
            var end = index + 1 < offsets.Count ? offsets[index + 1] : bytes.Length;
            var blob = bytes[start..end];
            if (blob.AsSpan().IndexOf(NativeId) < 0) continue;
            // PIDL-backed application links keep activation arguments in the
            // target's property bag, not necessarily IShellLink.GetArguments.
            var text = Encoding.Unicode.GetString(blob) + "\0" + Encoding.Unicode.GetString(blob, 1, blob.Length - 1);
            var match = Regex.Match(text, @"--chatgpt-shell=([A-Za-z0-9%._~!()*'/-]+)", RegexOptions.CultureInvariant);
            if (!match.Success) continue;
            try
            {
                using var action = JsonDocument.Parse(Uri.UnescapeDataString(match.Groups[1].Value));
                if (!action.RootElement.TryGetProperty("kind", out var kind)) continue;
                if (kind.GetString() is not ("new-chat" or "chat")) continue;
                if (!actions.Add(match.Value)) continue;
                links.Add(new(blob, kind.GetString() == "chat", match.Value));
            }
            catch (Exception exception) when (exception is JsonException or UriFormatException or InvalidOperationException) { }
        }
        return links.ToArray();
    }
}

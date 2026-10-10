using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class WindowsRecentChats
{
    internal sealed record Entry(string Title, string Arguments, long UpdatedAt);
    internal sealed record Snapshot(Entry[] Entries, string[] Errors);

    internal static async Task<Snapshot> ReadAsync()
    {
        var source = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "update", "WindowsRecentChats.js"));
        return Parse((await Program.RunScriptAsync(source, "windows-recent-chats")).Result);
    }

    internal static void WriteStatus(Entry[] entries, string[] errors)
    {
        Directory.CreateDirectory(Program.StateDirectory);
        var path = Path.Combine(Program.StateDirectory, "taskbar-history.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new { checkedAtUtc = DateTime.UtcNow, entries, errors }));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static Snapshot Parse(JsonElement result)
    {
        var entries = new List<Entry>();
        foreach (var item in result.GetProperty("entries").EnumerateArray())
        {
            var id = Text(item, "id");
            if (!Guid.TryParse(id, out _)) continue;
            var title = Text(item, "title").Trim();
            if (title.Length == 0) title = "New chat";
            if (title.Length > 160) title = title[..160];
            var time = item.TryGetProperty("updatedAt", out var updatedAt) ? Timestamp(updatedAt) : 0;
            if (time <= 0) continue; // Unknown activity cannot honestly be ranked.
            string arguments;
            if (Text(item, "kind") == "codex")
            {
                var host = Text(item, "hostId");
                if (host != "local" && !host.StartsWith("remote-control:", StringComparison.Ordinal)) continue;
                var uri = $"codex://threads/{id}" + (host == "local" ? "" : "?hostId=" + Uri.EscapeDataString(host));
                arguments = "session-shell " + Convert.ToBase64String(Encoding.UTF8.GetBytes(uri));
            }
            else if (Text(item, "kind") == "chatgpt")
            {
                var accountId = Text(item, "accountId");
                if (string.IsNullOrWhiteSpace(accountId)) continue;
                var action = JsonSerializer.Serialize(new { kind = "chat", accountId, conversationId = id,
                    projectId = item.TryGetProperty("projectId", out var project) && project.ValueKind == JsonValueKind.String ? project.GetString() : null });
                arguments = "native-shell " + Convert.ToBase64String(Encoding.UTF8.GetBytes("--chatgpt-shell=" + Uri.EscapeDataString(action)));
            }
            else continue;
            entries.Add(new(title, arguments, time));
        }
        return new(Rank(entries), result.GetProperty("errors").EnumerateArray().Select(e => e.GetString() ?? "Unknown history error").ToArray());
    }

    internal static Entry[] Rank(IEnumerable<Entry> entries) => entries.OrderByDescending(e => e.UpdatedAt)
        .ThenBy(e => e.Arguments, StringComparer.Ordinal).DistinctBy(e => e.Arguments).Take(8).ToArray();

    private static string Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    internal static long Timestamp(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0 && number < 253402300800000)
            return (long)(number < 100000000000 ? number * 1000 : number);
        return value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var date) ? date.ToUnixTimeMilliseconds() : 0;
    }

    internal static string DecodeSessionAction(string encoded)
    {
        var text = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "codex" || uri.Host != "threads" ||
            !Guid.TryParse(uri.AbsolutePath.Trim('/'), out _) || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0 || !uri.IsDefaultPort)
            throw new ArgumentException("Unsupported Codex session destination.");
        if (uri.Query.Length != 0 && (!uri.Query.StartsWith("?hostId=", StringComparison.Ordinal) || uri.Query.Contains('&') ||
            !Uri.UnescapeDataString(uri.Query[8..]).StartsWith("remote-control:", StringComparison.Ordinal)))
            throw new ArgumentException("Unsupported Codex session host.");
        return text;
    }
}

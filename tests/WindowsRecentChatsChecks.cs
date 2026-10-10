using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class WindowsRecentChatsChecks
{
    internal static void Run()
    {
        using var fixture = JsonDocument.Parse("""
            {"entries":[
              {"kind":"codex","id":"01a1201b-c75a-7331-ae6d-217aaa904e03","hostId":"local","title":"Local","updatedAt":1791547200},
              {"kind":"chatgpt","id":"6abb71f9-82d0-83eb-814a-749d86f18150","accountId":"account","projectId":"project","title":"Newest ChatGPT","updatedAt":"2026-10-10T12:00:00Z"},
              {"kind":"codex","id":"01a1201b-c75a-7331-ae6d-217aaa904e03","hostId":"remote-control:example","title":"Remote","updatedAt":1791633600000},
              {"kind":"codex","id":"01a1201b-c75a-7331-ae6d-217aaa904e03","hostId":"local","title":"Older duplicate","updatedAt":1791547100},
              {"kind":"chatgpt","id":"bad-id","accountId":"account","title":"Malformed","updatedAt":1791633600},
              {"kind":"codex","id":"01a1201b-c75a-7331-ae6d-217aaa904e03","hostId":"local","title":"No timestamp","updatedAt":null}
            ],"errors":["offline host"]}
            """);
        var snapshot = WindowsRecentChats.Parse(fixture.RootElement);
        if (snapshot.Entries.Length != 3 || snapshot.Entries[0].Title != "Newest ChatGPT" || snapshot.Entries[1].Title != "Remote" || snapshot.Entries[2].Title != "Local")
            throw new Exception("Mixed history must sort by activity, normalize timestamps, deduplicate per host, and reject invalid metadata.");
        if (snapshot.Errors.Single() != "offline host") throw new Exception("History gaps must remain visible.");
        var native = Program.DecodeNativeShellAction(snapshot.Entries[0].Arguments.Split(' ')[1]);
        using var action = JsonDocument.Parse(Uri.UnescapeDataString(native["--chatgpt-shell=".Length..]));
        if (action.RootElement.GetProperty("accountId").GetString() != "account" || action.RootElement.GetProperty("projectId").GetString() != "project")
            throw new Exception("ChatGPT destinations must retain account and project routing.");
        foreach (var entry in snapshot.Entries.Skip(1))
        {
            var uri = WindowsRecentChats.DecodeSessionAction(entry.Arguments.Split(' ')[1]);
            if (!uri.StartsWith("codex://threads/", StringComparison.Ordinal)) throw new Exception("Codex destinations must use native thread deep links.");
        }
        var newest = snapshot.Entries[2] with { Title = "Newest Codex", UpdatedAt = snapshot.Entries[0].UpdatedAt + 1 };
        if (WindowsRecentChats.Rank([..snapshot.Entries, newest])[0].Title != "Newest Codex")
            throw new Exception("Codex must rank above ChatGPT when its activity is newer.");
        if (WindowsRecentChats.Rank(Enumerable.Range(0, 20).Select(i => new WindowsRecentChats.Entry(i.ToString(), i.ToString(), i))).Length != 8)
            throw new Exception("The history limit applies globally across types.");
        foreach (var uri in new[] { "https://example.com", "codex://threads/not-a-thread", "codex://threads/01a1201b-c75a-7331-ae6d-217aaa904e03?hostId=local&command=quit" })
        {
            try
            {
                WindowsRecentChats.DecodeSessionAction(Convert.ToBase64String(Encoding.UTF8.GetBytes(uri)));
                throw new Exception("The session router accepted an unrelated destination.");
            }
            catch (ArgumentException) { }
        }
    }
}

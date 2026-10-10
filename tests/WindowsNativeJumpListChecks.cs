using System.Text;

namespace ClaudexYourself;

internal static class WindowsNativeJumpListChecks
{
    internal static void Run()
    {
        byte[] Link(string action, string appId = "OpenAI.Codex_2p2nqsd0c76g0!App") =>
            [..Convert.FromHexString("4C0000000114020000000000C000000000000046"),
             ..Encoding.Unicode.GetBytes(appId + "\0--chatgpt-shell=" + Uri.EscapeDataString(action) + "\0")];
        var newChat = Link("{\"kind\":\"new-chat\"}");
        var recent = Link("{\"kind\":\"chat\",\"accountId\":\"account\",\"conversationId\":\"chat\"}");
        var links = WindowsNativeJumpList.ExtractLinks([0, 1, 2, ..newChat, ..recent, ..newChat]);
        if (links.Length != 2 || links[0].RecentChat || !links[1].RecentChat)
            throw new Exception("Native Jump List tasks and recent chats must merge without duplicates.");
        if (WindowsNativeJumpList.ExtractLinks(Link("{\"kind\":\"new-chat\"}", "Unrelated.Application")).Length != 0)
            throw new Exception("An unrelated app's shell links must not enter Claudex's Jump List.");
        if (WindowsNativeJumpList.ExtractLinks(Link("not JSON")).Length != 0 || WindowsNativeJumpList.ExtractLinks(newChat[..10]).Length != 0)
            throw new Exception("Malformed or truncated native links must be rejected.");
        // PIDL property strings can begin on either byte alignment in a container.
        byte[] odd = [..newChat[..20], 0, ..newChat[20..]];
        if (WindowsNativeJumpList.ExtractLinks(odd).Length != 1)
            throw new Exception("Odd-aligned native activation data must be preserved.");
        if (WindowsNativeJumpList.ExtractLinks(new byte[2 * 1024 * 1024 + 1]).Length != 0)
            throw new Exception("Native Jump List input must remain bounded.");
        foreach (var link in links)
        {
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(link.Arguments));
            if (Program.DecodeNativeShellAction(encoded) != link.Arguments)
                throw new Exception("Native account/chat routing must survive launcher encoding unchanged.");
        }
        try
        {
            Program.DecodeNativeShellAction(Convert.ToBase64String(Encoding.UTF8.GetBytes("--some-other-command")));
            throw new Exception("The native shell router accepted an unrelated command.");
        }
        catch (ArgumentException) { }
        if (OperatingSystem.IsWindows())
        {
            var shellLink = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046"), true)!)!;
            try
            {
                WindowsShortcut.SetStringProperty(shellLink, new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2, "Fallback title");
                if (WindowsJumpList.ReadNativeTitle(shellLink) != "Fallback title") throw new Exception("Standard Shell Link titles must remain supported.");
                WindowsShortcut.SetStringProperty(shellLink, new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 27, "Paulebazi Erklärung");
                if (WindowsJumpList.ReadNativeTitle(shellLink) != "Paulebazi Erklärung") throw new Exception("Native provided titles must win over generic shortcut identity/title fields.");
            }
            finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shellLink); }
        }
    }
}

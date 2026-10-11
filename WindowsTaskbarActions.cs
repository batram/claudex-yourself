namespace ClaudexYourself;

internal enum CodexLaunchMode { Closed, Vanilla, Claudex }

internal static class WindowsTaskbarActions
{
    internal static readonly string[] Names = ["claudex", "vanilla", "restart", "quit"];

    internal static async Task RunAsync(string action, CodexLaunchMode mode, Func<Task> quitControlled,
        Func<Task> quitVanilla, Func<Task> waitForExit, Action openClaudex, Action openVanilla)
    {
        if (!Names.Contains(action)) throw new ArgumentException("Taskbar action must be claudex, vanilla, restart, or quit.");
        var mustQuit = mode == CodexLaunchMode.Claudex && action is "vanilla" or "restart" or "quit"
            || mode == CodexLaunchMode.Vanilla && action is "claudex" or "restart" or "quit";
        if (mustQuit)
        {
            await (mode == CodexLaunchMode.Claudex ? quitControlled() : quitVanilla());
            await waitForExit();
        }
        if (action is "claudex" or "restart") openClaudex();
        else if (action == "vanilla") openVanilla();
    }
}

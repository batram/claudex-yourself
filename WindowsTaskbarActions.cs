namespace ClaudexYourself;

internal enum CodexLaunchMode { Closed, Vanilla, Claudex }

internal static class WindowsTaskbarActions
{
    internal static readonly string[] Names = ["claudex", "vanilla", "restart", "quit"];

    internal static bool RequiresQuit(string action, CodexLaunchMode mode) =>
        mode == CodexLaunchMode.Claudex && action is "vanilla" or "restart" or "quit"
        || mode == CodexLaunchMode.Vanilla && action is "claudex" or "restart" or "quit";

    internal static async Task RunAsync(string action, CodexLaunchMode mode, Func<Task> quitControlled,
        Func<Task> quitVanilla, Func<Task> waitForExit, Action openClaudex, Action openVanilla, Action<string>? report = null)
    {
        if (!Names.Contains(action)) throw new ArgumentException("Taskbar action must be claudex, vanilla, restart, or quit.");
        var mustQuit = RequiresQuit(action, mode);
        if (mustQuit)
        {
            report?.Invoke("Requesting normal quit.");
            await (mode == CodexLaunchMode.Claudex ? quitControlled() : quitVanilla());
            report?.Invoke("Quit requested; waiting for package processes to exit.");
            await waitForExit();
            report?.Invoke("Package processes exited and quiet period completed.");
        }
        if (action is "claudex" or "restart")
        {
            report?.Invoke("Opening controlled Codex; waiting for renderer readiness.");
            openClaudex();
        }
        else if (action == "vanilla")
        {
            report?.Invoke("Opening vanilla Codex.");
            openVanilla();
        }
        report?.Invoke("Action completed.");
    }
}

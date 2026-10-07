namespace ClaudexYourself;

internal static class WindowsTaskbarActionsChecks
{
    internal static async Task RunAsync()
    {
        foreach (var mode in Enum.GetValues<CodexLaunchMode>())
        foreach (var action in WindowsTaskbarActions.Names)
        {
            var calls = new List<string>();
            Task Record(string call) { calls.Add(call); return Task.CompletedTask; }
            await WindowsTaskbarActions.RunAsync(action, mode, () => Record("quit-controlled"), () => Record("quit-vanilla"),
                () => Record("wait"), () => calls.Add("open-claudex"), () => calls.Add("open-vanilla"));
            string[] expected = (mode, action) switch
            {
                (CodexLaunchMode.Closed, "quit") => [],
                (CodexLaunchMode.Closed, "vanilla") => ["open-vanilla"],
                (CodexLaunchMode.Closed, _) => ["open-claudex"],
                (CodexLaunchMode.Vanilla, "vanilla") => ["open-vanilla"],
                (CodexLaunchMode.Vanilla, "claudex") => ["quit-vanilla", "wait", "open-claudex"],
                (CodexLaunchMode.Vanilla, "restart") => ["quit-vanilla", "wait", "open-claudex"],
                (CodexLaunchMode.Vanilla, "quit") => ["quit-vanilla", "wait"],
                (CodexLaunchMode.Claudex, "claudex") => ["open-claudex"],
                (CodexLaunchMode.Claudex, "vanilla") => ["quit-controlled", "wait", "open-vanilla"],
                (CodexLaunchMode.Claudex, "restart") => ["quit-controlled", "wait", "open-claudex"],
                (CodexLaunchMode.Claudex, "quit") => ["quit-controlled", "wait"],
                _ => throw new Exception("Unexpected test combination.")
            };
            if (!calls.SequenceEqual(expected)) throw new Exception($"Taskbar sequence failed: {mode}, {action}.");
        }
        foreach (var action in new[] { "claudex", "vanilla", "restart", "quit" })
        {
            var mode = action == "claudex" ? CodexLaunchMode.Vanilla : CodexLaunchMode.Claudex;
            foreach (var failQuit in new[] { false, true })
            {
                var opened = false;
                Task Fail() => throw new OperationCanceledException("Quit cancelled");
                try
                {
                    await WindowsTaskbarActions.RunAsync(action, mode, failQuit ? Fail : () => Task.CompletedTask,
                        failQuit ? Fail : () => Task.CompletedTask, failQuit ? () => Task.CompletedTask : Fail,
                        () => opened = true, () => opened = true);
                    throw new Exception("Taskbar action ignored quit cancellation.");
                }
                catch (OperationCanceledException) { }
                if (opened) throw new Exception("Taskbar action launched after cancellation.");
            }
        }
        var sideEffect = false;
        try
        {
            await WindowsTaskbarActions.RunAsync("unknown", CodexLaunchMode.Closed, () => Task.CompletedTask,
                () => Task.CompletedTask, () => Task.CompletedTask, () => sideEffect = true, () => sideEffect = true);
            throw new Exception("Unknown taskbar action was accepted.");
        }
        catch (ArgumentException) { }
        if (sideEffect) throw new Exception("Unknown action caused a side effect.");
    }
}

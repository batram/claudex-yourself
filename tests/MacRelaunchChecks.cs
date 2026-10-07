namespace ClaudexYourself;

internal static class MacRelaunchChecks
{
    internal static async Task RunAsync()
    {
        Assert(MacRelaunchWatcher.IsUpdatedBuild("9999", "10000"), "builds compare numerically");
        Assert(MacRelaunchWatcher.IsUpdatedBuild("26.930.1", "26.1001.1"), "dotted versions");
        foreach (var version in new[] { "13232", "13231", "unknown", "" })
            Assert(!MacRelaunchWatcher.IsUpdatedBuild("13232", version), "same, older, or unknown builds must not trigger a restart");

        var actions = new List<string>();
        Task Record(string action) { actions.Add(action); return Task.CompletedTask; }
        await MacRelaunchWatcher.RestoreAsync(() => Task.FromResult(true), () => Record("quit"), () => Record("exit"), () => Record("launch"));
        Assert(actions.Count == 0, "already controlled update does not quit again");
        await MacRelaunchWatcher.RestoreAsync(() => Task.FromResult(false), () => Record("quit"), () => Record("exit"), () => Record("launch"));
        Assert(string.Join(",", actions) == "quit,exit,launch", "wait for normal exit before controlled launch");

        foreach (var failedAction in new[] { "quit", "exit", "launch" })
        {
            actions.Clear();
            Task Step(string action)
            {
                actions.Add(action);
                return action == failedAction ? Task.FromException(new TimeoutException("fixture")) : Task.CompletedTask;
            }
            try
            {
                await MacRelaunchWatcher.RestoreAsync(() => Task.FromResult(false), () => Step("quit"), () => Step("exit"), () => Step("launch"));
                throw new Exception("Relaunch failure was hidden.");
            }
            catch (TimeoutException) { }
            Assert(actions[^1] == failedAction, "a failed or cancelled quit/exit stops recovery; launch failures stay visible");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("macOS relaunch check failed: " + message);
    }
}

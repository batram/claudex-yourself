namespace ClaudexYourself;

internal static class VanillaLaunch
{
    internal static bool HasDebuggingArguments(string commandLine) =>
        System.Text.RegularExpressions.Regex.IsMatch(commandLine, @"(?:^|\s)--remote-debugging-(?:port(?:=|\s+)\d+|pipe)(?:\s|$)");

    // Keep the quit/wait/activate sequence testable without closing the user's session.
    internal static async Task RunAsync(bool controlledRunning, Func<Task> requestQuit,
        Func<Task> waitForExit, Action activate)
    {
        if (controlledRunning)
        {
            await requestQuit();
            await waitForExit(); // Failure or quit cancellation must never fall through to activation.
        }
        activate();
    }
}

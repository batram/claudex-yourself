namespace ClaudexYourself;

internal static class TrayIconReadinessChecks
{
    internal static async Task RunAsync()
    {
        var reveals = 0;
        var delays = 0;
        var icon = new object();
        // Twenty-four polling intervals represent six seconds: beyond the old
        // five-second startup limit. The injected delay keeps this test fast.
        var found = await TrayIconReadiness.WaitAsync<object>(() => delays >= 24 ? [icon] : [],
            () => { reveals++; return Task.CompletedTask; }, TimeSpan.FromSeconds(30),
            _ => { delays++; return Task.CompletedTask; });
        if (!ReferenceEquals(found, icon) || reveals != 24 || delays != 24)
            throw new Exception("Delayed tray readiness did not retry hidden-icon discovery.");

        var overflowReady = false;
        found = await TrayIconReadiness.WaitAsync<object>(() => overflowReady ? [icon] : [],
            () => { overflowReady = true; return Task.CompletedTask; }, TimeSpan.Zero);
        if (!ReferenceEquals(found, icon)) throw new Exception("Hidden tray icon was not discovered after revealing overflow.");

        await ExpectFailureAsync([], "found 0");
        await ExpectFailureAsync([icon, new object()], "found 2");
        var revealed = false;
        found = await TrayIconReadiness.WaitAsync<object>(() => [icon],
            () => { revealed = true; return Task.CompletedTask; }, TimeSpan.Zero);
        if (revealed || !ReferenceEquals(found, icon)) throw new Exception("Visible tray icon unnecessarily opened overflow.");
    }

    private static async Task ExpectFailureAsync(object[] icons, string message)
    {
        try
        {
            await TrayIconReadiness.WaitAsync(() => icons, () => Task.CompletedTask, TimeSpan.Zero);
            throw new Exception("Missing or ambiguous tray icon was accepted.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(message) &&
            exception.Message.Contains("No quit action was sent.")) { }
    }
}

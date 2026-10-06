namespace ClaudexYourself;

internal static class VanillaLaunchChecks
{
    internal static async Task RunAsync()
    {
        foreach (var arguments in new[] { "--remote-debugging-port=9229", "--remote-debugging-port 9333", "--remote-debugging-pipe" })
            if (!VanillaLaunch.HasDebuggingArguments($"\"C:\\Program Files\\ChatGPT.exe\" {arguments} --other"))
                throw new InvalidOperationException("Debugging launch detection failed: " + arguments);
        if (VanillaLaunch.HasDebuggingArguments("ChatGPT.exe --other") || VanillaLaunch.HasDebuggingArguments("ChatGPT.exe --other=--remote-debugging-port=9229"))
            throw new InvalidOperationException("Vanilla launch was misidentified as controlled.");
        var calls = new List<string>();
        Task Quit() { calls.Add("quit"); return Task.CompletedTask; }
        Task Wait() { calls.Add("wait"); return Task.CompletedTask; }
        void Activate() => calls.Add("activate");
        await VanillaLaunch.RunAsync(false, Quit, Wait, Activate);
        if (!calls.SequenceEqual(["activate"])) throw new InvalidOperationException("Vanilla launch must preserve an existing vanilla session.");
        calls.Clear();
        await VanillaLaunch.RunAsync(true, Quit, Wait, Activate);
        if (!calls.SequenceEqual(["quit", "wait", "activate"])) throw new InvalidOperationException("Vanilla restart ordering failed.");
        calls.Clear();
        try
        {
            await VanillaLaunch.RunAsync(true, Quit, () => throw new TimeoutException("Quit cancelled"), Activate);
            throw new InvalidOperationException("Vanilla restart ignored quit cancellation.");
        }
        catch (TimeoutException) { }
        if (!calls.SequenceEqual(["quit"])) throw new InvalidOperationException("Vanilla restart activated after quit cancellation.");
        calls.Clear();
        try
        {
            await VanillaLaunch.RunAsync(true, () => throw new InvalidOperationException("Bridge unavailable"), Wait, Activate);
            throw new Exception("Vanilla restart ignored quit failure.");
        }
        catch (InvalidOperationException) { }
        if (calls.Count != 0) throw new InvalidOperationException("Vanilla restart continued after quit failure.");
    }
}

namespace ClaudexYourself;

internal static class VanillaLaunchChecks
{
    internal static Task RunAsync()
    {
        foreach (var arguments in new[] { "--remote-debugging-port=9229", "--remote-debugging-port 9333", "--remote-debugging-pipe" })
            if (!VanillaLaunch.HasDebuggingArguments($"\"C:\\Program Files\\ChatGPT.exe\" {arguments} --other"))
                throw new InvalidOperationException("Debugging launch detection failed: " + arguments);
        if (VanillaLaunch.HasDebuggingArguments("ChatGPT.exe --other") || VanillaLaunch.HasDebuggingArguments("ChatGPT.exe --other=--remote-debugging-port=9229"))
            throw new InvalidOperationException("Vanilla launch was misidentified as controlled.");
        return Task.CompletedTask;
    }
}

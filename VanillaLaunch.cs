namespace ClaudexYourself;

internal static class VanillaLaunch
{
    internal static bool HasDebuggingArguments(string commandLine) =>
        System.Text.RegularExpressions.Regex.IsMatch(commandLine, @"(?:^|\s)--remote-debugging-(?:port(?:=|\s+)\d+|pipe)(?:\s|$)");
}

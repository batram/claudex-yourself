using System.Diagnostics;

namespace ClaudexYourself;

internal sealed record UserscriptMetadata(
    string Name,
    string Id,
    string Version,
    string Description,
    string RunAt,
    string[] Platforms,
    string[] Grants,
    string[] TestedCodexVersions)
{
    private const string StartMarker = "// ==ClaudexUserScript==";
    private const string EndMarker = "// ==/ClaudexUserScript==";

    public static UserscriptMetadata Parse(string source)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() == StartMarker);
        var end = Array.FindIndex(lines, line => line.Trim() == EndMarker);
        if (start < 0 || end <= start) throw new FormatException("Userscript metadata header is missing or incomplete.");

        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in lines[(start + 1)..end])
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("// @", StringComparison.Ordinal)) continue;
            var content = line[4..].Trim();
            var separator = content.IndexOfAny([' ', '\t']);
            if (separator <= 0) throw new FormatException($"Invalid userscript metadata line: {rawLine}");
            var key = content[..separator];
            var value = content[(separator + 1)..].Trim();
            if (value.Length == 0) throw new FormatException($"Userscript metadata @{key} has no value.");
            if (!values.TryGetValue(key, out var entries)) values[key] = entries = [];
            entries.Add(value);
        }

        string One(string key) => values.TryGetValue(key, out var entries) && entries.Count == 1
            ? entries[0]
            : throw new FormatException($"Userscript metadata requires exactly one @{key} value.");
        string[] Many(string key) => values.TryGetValue(key, out var entries) ? entries.ToArray() : [];
        string[] Csv(string key) => Many(key).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var metadata = new UserscriptMetadata(
            One("name"),
            NormalizeId(One("id")),
            One("version"),
            One("description"),
            One("run-at"),
            Csv("platform"),
            Csv("grant"),
            Many("codex-tested").Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        if (metadata.Platforms.Length == 0) throw new FormatException("Userscript metadata requires @platform.");
        if (metadata.Grants.Length == 0) throw new FormatException("Userscript metadata requires @grant.");
        if (metadata.RunAt != "renderer-ready") throw new FormatException("The only supported @run-at value is renderer-ready.");
        return metadata;
    }

    public static string NormalizeId(string value)
    {
        var id = value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? value[..^3] : value;
        if (id.Length is 0 or > 80 || id.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new FormatException("Userscript @id may contain only letters, digits, '-' and '_'.");
        return id;
    }

    public CompatibilityResult Compatibility(string currentCodexVersion)
    {
        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "unsupported";
        var platformSupported = Platforms.Contains(platform, StringComparer.OrdinalIgnoreCase);
        var tested = TestedCodexVersions.Contains(currentCodexVersion, StringComparer.OrdinalIgnoreCase);
        var status = !platformSupported ? "unsupported_platform" : tested ? "tested" : "untested_current_version";
        return new CompatibilityResult(status, currentCodexVersion, tested, platformSupported);
    }

    public static string AddTestedVersion(string source, string codexVersion)
    {
        var metadata = Parse(source);
        if (metadata.TestedCodexVersions.Contains(codexVersion, StringComparer.OrdinalIgnoreCase)) return source;
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var marker = EndMarker;
        var index = source.IndexOf(marker, StringComparison.Ordinal);
        return source.Insert(index, $"// @codex-tested  {codexVersion}{newline}");
    }

    public static string CurrentCodexVersion()
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -Command \"$p = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1; if ($p) { $p.Version.ToString() }\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not query the installed Codex version.");
            var version = process.StandardOutput.ReadToEnd().Trim();
            var error = process.StandardError.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || version.Length == 0) throw new InvalidOperationException($"Could not determine the installed Codex version. {error}".Trim());
            return version;
        }
        if (OperatingSystem.IsMacOS())
        {
            var bundle = new[] { "/Applications/Codex.app", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications/Codex.app") }.FirstOrDefault(Directory.Exists)
                ?? throw new FileNotFoundException("Codex.app was not found.");
            using var process = Process.Start(new ProcessStartInfo("/usr/bin/defaults")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "read", Path.Combine(bundle, "Contents/Info"), "CFBundleVersion" }
            }) ?? throw new InvalidOperationException("Could not query Codex.app version.");
            var version = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();
            if (process.ExitCode != 0 || version.Length == 0) throw new InvalidOperationException("Could not determine Codex.app version.");
            return version;
        }
        throw new PlatformNotSupportedException("Codex Desktop version detection supports Windows and macOS.");
    }

    internal sealed record CompatibilityResult(string Status, string CurrentCodexVersion, bool Tested, bool PlatformSupported);
}

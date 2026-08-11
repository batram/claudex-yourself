using System.Text.Json;

namespace ClaudexYourself;

internal static class AutoloadScripts
{
    private static readonly string ConfigurationPath = Path.Combine(Program.StateDirectory, "autoload.json");
    private static readonly string StatusPath = Path.Combine(Program.StateDirectory, "autoload-status.json");
    private static readonly object Gate = new();

    public static bool IsEnabled(string name) => Load().Contains(NormalizeName(name));

    public static AutoloadSetting Set(string name, bool enabled)
    {
        name = NormalizeName(name);
        var scriptPath = Path.Combine(Program.UserScriptDirectory, name + ".js");
        if (enabled && !File.Exists(scriptPath)) throw new FileNotFoundException($"User script '{name}' does not exist.");
        lock (Gate)
        {
            var names = Load();
            if (enabled) names.Add(name); else names.Remove(name);
            Save(names);
        }
        return new AutoloadSetting(name, enabled);
    }

    public static string[] EnabledNames() => Load().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static async Task<int> RunWorkerAsync()
    {
        Directory.CreateDirectory(Program.StateDirectory);
        var names = EnabledNames();
        var started = DateTime.UtcNow;
        object status;
        try
        {
            if (names.Length == 0)
            {
                status = new { state = "completed", started_at_utc = started, completed_at_utc = DateTime.UtcNow, scripts = Array.Empty<object>() };
            }
            else
            {
                await WaitForRendererAsync(TimeSpan.FromSeconds(60));
                var codexVersion = UserscriptMetadata.CurrentCodexVersion();
                var results = new List<object>();
                foreach (var name in names)
                {
                    var path = Path.Combine(Program.UserScriptDirectory, name + ".js");
                    if (!File.Exists(path))
                    {
                        results.Add(new { name, state = "missing" });
                        continue;
                    }
                    try
                    {
                        var source = await File.ReadAllTextAsync(path);
                        var metadata = UserscriptMetadata.Parse(source);
                        if (!metadata.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Userscript @id '{metadata.Id}' does not match filename '{name}'.");
                        var compatibility = metadata.Compatibility(codexVersion);
                        if (!compatibility.PlatformSupported)
                        {
                            results.Add(new { name, state = "skipped", compatibility });
                            continue;
                        }
                        var result = await Program.RunScriptAsync(source, name);
                        results.Add(new { name, state = "completed", compatibility, logs = result.Logs, result = result.Result });
                    }
                    catch (Exception exception)
                    {
                        results.Add(new { name, state = "failed", error = exception.Message });
                    }
                }
                status = new { state = "completed", started_at_utc = started, completed_at_utc = DateTime.UtcNow, scripts = results };
            }
        }
        catch (Exception exception)
        {
            status = new { state = "failed", started_at_utc = started, completed_at_utc = DateTime.UtcNow, error = exception.Message };
        }
        await WriteAtomicAsync(StatusPath, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    public static async Task<object> ReadStatusAsync()
    {
        if (!File.Exists(StatusPath)) return new { state = "not_run" };
        return JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(StatusPath));
    }

    private static async Task WaitForRendererAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var json = await client.GetStringAsync("http://127.0.0.1:9229/json/list");
                if (json.Contains("\"type\": \"page\"", StringComparison.OrdinalIgnoreCase)
                    || json.Contains("\"type\":\"page\"", StringComparison.OrdinalIgnoreCase)) return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(500);
        }
        throw new TimeoutException("Codex renderer did not become ready within 60 seconds.");
    }

    private static HashSet<string> Load()
    {
        lock (Gate)
        {
            if (!File.Exists(ConfigurationPath)) return new(StringComparer.OrdinalIgnoreCase);
            var values = JsonSerializer.Deserialize<string[]>(File.ReadAllText(ConfigurationPath)) ?? [];
            return new(values.Select(NormalizeName), StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save(HashSet<string> names)
    {
        Directory.CreateDirectory(Program.StateDirectory);
        var json = JsonSerializer.Serialize(names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), new JsonSerializerOptions { WriteIndented = true });
        var temporary = ConfigurationPath + ".tmp";
        File.WriteAllText(temporary, json);
        File.Move(temporary, ConfigurationPath, overwrite: true);
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }

    private static string NormalizeName(string name)
    {
        var stem = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        if (stem.Length is 0 or > 80 || stem.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new ArgumentException("Script name may contain only letters, digits, '-' and '_'.");
        return stem;
    }

    internal sealed record AutoloadSetting(string Name, bool Enabled);
}

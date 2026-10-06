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
        // A controlled launch can be activated repeatedly. Only one watcher owns autoload.
        FileStream workerLock;
        try { workerLock = new FileStream(Path.Combine(Program.StateDirectory, "autoload-worker.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 0; }
        using var workerLease = workerLock;
        var started = DateTime.UtcNow;
        var windows = new Dictionary<string, WindowState>();
        var unavailableSince = (DateTime?)null;
        try
        {
            await WaitForRendererAsync(TimeSpan.FromSeconds(60));
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            while (true)
            {
                JsonElement[] targets;
                try
                {
                    using var document = JsonDocument.Parse(await DevToolsLoopback.ReadMainTargetListAsync(client));
                    targets = document.RootElement.EnumerateArray().Where(target =>
                        target.GetProperty("type").GetString() == "page"
                        && RendererDevTools.IsUserscriptPage(target.GetProperty("url").GetString()))
                        .Select(target => target.Clone()).ToArray();
                    unavailableSince = null;
                }
                catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {
                    unavailableSince ??= DateTime.UtcNow;
                    if (DateTime.UtcNow - unavailableSince > TimeSpan.FromSeconds(15)) break;
                    await Task.Delay(1000);
                    continue;
                }
                var names = EnabledNames();
                var changed = windows.Count == 0;
                var activeIds = targets.Select(target => target.GetProperty("id").GetString()!).ToHashSet();
                foreach (var closed in windows.Keys.Where(id => !activeIds.Contains(id)).ToArray())
                { windows.Remove(closed); changed = true; }
                foreach (var target in targets)
                {
                    var id = target.GetProperty("id").GetString()!;
                    var socket = target.GetProperty("webSocketDebuggerUrl").GetString()!;
                    string token;
                    try
                    {
                        token = await RendererDevTools.EvaluateStringAsync("document.readyState === 'complete' && document.body && typeof window.electronBridge?.sendMessageFromView === 'function' ? (window[Symbol.for('claudex-yourself.autoload-document')] ??= crypto.randomUUID()) : ''", TimeSpan.FromSeconds(2), socket);
                    }
                    catch (Exception exception) when (exception is System.Net.WebSockets.WebSocketException or OperationCanceledException or InvalidOperationException) { continue; }
                    if (token.Length == 0) continue;
                    if (!windows.TryGetValue(id, out var window) || window.Token != token)
                    { windows[id] = window = new WindowState(token, target.GetProperty("url").GetString()!); changed = true; }
                    // The worker can survive an updater relaunch into a new app build.
                    var codexVersion = names.Any(name => !window.Scripts.ContainsKey(name))
                        ? UserscriptMetadata.CurrentCodexVersion() : null;
                    foreach (var disabled in window.Scripts.Keys.Where(name => !names.Contains(name)).ToArray())
                    { window.Scripts.Remove(disabled); changed = true; }
                    foreach (var name in names)
                    {
                        if (window.Scripts.ContainsKey(name)) continue;
                        var path = Path.Combine(Program.UserScriptDirectory, name + ".js");
                        try
                        {
                            if (!File.Exists(path)) { window.Scripts[name] = new { name, state = "missing" }; continue; }
                            var source = await File.ReadAllTextAsync(path);
                            var metadata = UserscriptMetadata.Parse(source);
                            if (!metadata.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Userscript @id '{metadata.Id}' does not match filename '{name}'.");
                            var compatibility = metadata.Compatibility(codexVersion!);
                            if (!compatibility.PlatformSupported) { window.Scripts[name] = new { name, state = "skipped", compatibility }; continue; }
                            var result = await Program.RunScriptAsync(source, name, socket);
                            window.Scripts[name] = new { name, state = "completed", compatibility, logs = result.Logs, result = result.Result };
                        }
                        catch (Exception exception) { window.Scripts[name] = new { name, state = "failed", error = exception.Message }; }
                        finally { changed = true; }
                    }
                }
                if (changed) await SaveStatusAsync("watching", started, windows);
                await Task.Delay(1000);
            }
            await SaveStatusAsync("stopped", started, windows);
        }
        catch (Exception exception)
        {
            await WriteAtomicAsync(StatusPath, JsonSerializer.Serialize(new { state = "failed", started_at_utc = started, completed_at_utc = DateTime.UtcNow, error = exception.Message }));
            return 1;
        }
        return 0;
    }

    private sealed class WindowState(string token, string url)
    {
        public string Token { get; } = token;
        public string Url { get; } = url;
        public Dictionary<string, object> Scripts { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Task SaveStatusAsync(string state, DateTime started, Dictionary<string, WindowState> windows) =>
        WriteAtomicAsync(StatusPath, JsonSerializer.Serialize(new {
            state, started_at_utc = started, updated_at_utc = DateTime.UtcNow,
            scripts = windows.Values.FirstOrDefault(window => RendererDevTools.IsMainCodexPage(window.Url))?.Scripts.Values.ToArray() ?? [],
            windows = windows.Select(pair => new { target_id = pair.Key, url = pair.Value.Url, scripts = pair.Value.Scripts.Values.ToArray() }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true }));

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
                await DevToolsLoopback.ReadMainTargetListAsync(client);
                return;
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            catch (InvalidOperationException) { }
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

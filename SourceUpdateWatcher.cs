using System.Diagnostics;
using System.Text.Json;

namespace ClaudexYourself;

// Reuses the controlled DevTools connection; no new listening port or renderer filesystem API.
internal static class SourceUpdateWatcher
{
    private const string Controller = "window[Symbol.for('claudex-yourself.userscript-settings')]";
    internal static void Start()
    {
        var executable = Environment.ProcessPath!;
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add("sources-watch");
        Process.Start(start)?.Dispose();
    }
    internal static async Task<int> RunAsync()
    {
        Directory.CreateDirectory(Program.StateDirectory);
        FileStream lease;
        try { lease = new FileStream(Path.Combine(Program.StateDirectory, "sources-watch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { return 0; }
        using var workerLease = lease;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var pending = new Dictionary<string, Task<object>>();
        var missed = 0;
        var scheduler = new SourceCheckScheduler(ScriptSources.Default);
        while (missed < 15)
        {
            try
            {
                using var targets = JsonDocument.Parse(await DevToolsLoopback.ReadMainTargetListAsync(client));
                missed = 0;
                var main = targets.RootElement.EnumerateArray().FirstOrDefault(t => t.GetProperty("type").GetString() == "page" && RendererDevTools.IsMainCodexPage(t.GetProperty("url").GetString()));
                if (main.ValueKind != JsonValueKind.Undefined) scheduler.Tick(main.GetProperty("id").GetString()!, DateTime.UtcNow);
                var snapshot = JsonSerializer.Serialize(ScriptSources.Default.Snapshot(), ScriptSources.Json);
                foreach (var target in targets.RootElement.EnumerateArray().Where(t => t.GetProperty("type").GetString() == "page" && RendererDevTools.IsUserscriptPage(t.GetProperty("url").GetString())))
                {
                    var socket = target.GetProperty("webSocketDebuggerUrl").GetString()!;
                    try
                    {
                        await RendererDevTools.EvaluateStringAsync($"JSON.stringify({Controller}?.updateSources?.({snapshot}) ?? null)", TimeSpan.FromSeconds(3), socket);
                        if (pending.TryGetValue(socket, out var task))
                        {
                            if (!task.IsCompleted) continue;
                            var result = JsonSerializer.Serialize(await task, ScriptSources.Json);
                            await RendererDevTools.EvaluateStringAsync($"JSON.stringify({Controller}?.receiveSourceResult?.({result}) ?? null)", TimeSpan.FromSeconds(3), socket);
                            pending.Remove(socket);
                        }
                        var raw = await RendererDevTools.EvaluateStringAsync($"JSON.stringify({Controller}?.takeSourceAction?.() ?? null)", TimeSpan.FromSeconds(3), socket);
                        if (raw != "null") pending[socket] = DispatchAsync(JsonSerializer.Deserialize<JsonElement>(raw));
                    }
                    catch (Exception error) { Console.Error.WriteLine("Settings connection: " + error.Message); }
                }
                var sockets = targets.RootElement.EnumerateArray().Where(t => t.TryGetProperty("webSocketDebuggerUrl", out _)).Select(t => t.GetProperty("webSocketDebuggerUrl").GetString()).ToHashSet();
                foreach (var socket in pending.Keys.Where(k => !sockets.Contains(k) && pending[k].IsCompleted).ToArray()) pending.Remove(socket);
            }
            catch (Exception error) { missed++; Console.Error.WriteLine("Settings controller: " + error.Message); }
            await Task.Delay(1000);
        }
        // An explicit install continues even if the originating window closes.
        await Task.WhenAll(pending.Values);
        await scheduler.DrainAsync();
        return 0;
    }
    internal static async Task<object> DispatchAsync(JsonElement request)
    {
        string? id = null;
        try
        {
            id = request.GetProperty("id").GetString();
            string Required(string key) => request.GetProperty(key).GetString() ?? throw new ArgumentException(key + " is required.");
            string? Optional(string key) => request.TryGetProperty(key, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
            var service = ScriptSources.Default;
            object value = Required("action") switch
            {
                "status" => service.Snapshot(),
                "check_claudex" => await service.CheckClaudexAsync(Optional("url")),
                "check_script" => await service.CheckScriptAsync(Required("name")),
                "set_source" => await service.SetSourceAsync(Optional("name"), Required("url")),
                "set_schedule" => await service.SetScheduleAsync(Optional("name"), request.GetProperty("onStartup").GetBoolean(), request.GetProperty("intervalMinutes").GetInt32(), request.TryGetProperty("inherit", out var inherit) && inherit.GetBoolean()),
                "preview" => await service.PreviewAsync(Required("url"), Optional("name")),
                "install" => await service.InstallAsync(Required("previewId"), request.GetProperty("autoload").GetBoolean(), true),
                _ => throw new ArgumentException("Unknown source action.")
            };
            return new { id, value };
        }
        catch (Exception error) { return new { id, error = error.Message }; }
    }
}

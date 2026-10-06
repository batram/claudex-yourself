using System.Text.Json;

namespace ClaudexYourself;

internal sealed partial class ScriptSources
{
    internal sealed record CheckSchedule(bool OnStartup, int IntervalMinutes)
    {
        internal static readonly CheckSchedule Default = new(true, 0);
        internal void Validate()
        {
            if (IntervalMinutes != 0 && (IntervalMinutes < 15 || IntervalMinutes > 10080))
                throw new ArgumentException("Check interval must be 0 (no repeat) or between 15 and 10080 minutes.");
        }
    }
    internal sealed record CheckTarget(string Key, string? Name, string Url, string InstalledVersion, CheckSchedule Schedule)
    {
        internal string Fingerprint => Hash(Key + "\n" + Url + "\n" + InstalledVersion);
    }
    internal sealed record SourceCheck(string Url, string InstalledVersion, DateTime CheckedAtUtc, JsonElement? Result, string? Error);
    private readonly Dictionary<string, Task<object>> inFlightChecks = new();
    private readonly object checksGate = new();
    private string ChecksPath => Path.Combine(root, "source-checks.json");

    private static CheckSchedule EffectiveSchedule(Configuration config, string name) =>
        config.ScriptSchedules?.GetValueOrDefault(name) ?? config.Schedule ?? CheckSchedule.Default;
    internal CheckSchedule DefaultSchedule => ReadConfig().Schedule ?? CheckSchedule.Default;
    internal async Task<object> SetScheduleAsync(string? name, bool onStartup, int intervalMinutes, bool inherit = false)
    {
        var schedule = new CheckSchedule(onStartup, intervalMinutes);
        schedule.Validate();
        using var lease = await LockAsync();
        var config = ReadConfig();
        if (name is null)
        {
            if (inherit) throw new ArgumentException("Only a script can inherit the default schedule.");
            config = config with { Schedule = schedule };
        }
        else
        {
            name = UserscriptMetadata.NormalizeId(name);
            if (!File.Exists(Path.Combine(Scripts, name + ".js"))) throw new FileNotFoundException("Install the script before changing its check schedule.");
            config = config with { ScriptSchedules = config.ScriptSchedules ?? new(StringComparer.OrdinalIgnoreCase) };
            if (inherit) config.ScriptSchedules.Remove(name); else config.ScriptSchedules[name] = schedule;
        }
        await WriteAsync(ConfigPath, config);
        return new { saved = true, name, schedule = name is null ? config.Schedule : EffectiveSchedule(config, name), inherited = inherit };
    }
    internal Dictionary<string, SourceCheck> ReadChecks() => File.Exists(ChecksPath)
        ? JsonSerializer.Deserialize<Dictionary<string, SourceCheck>>(File.ReadAllText(ChecksPath), Json) ?? [] : [];
    internal static SourceCheck? MatchingCheck(Dictionary<string, SourceCheck> checks, CheckTarget target) =>
        checks.TryGetValue(target.Key, out var check) && check.Url == target.Url && check.InstalledVersion == target.InstalledVersion ? check : null;
    private static List<CheckTarget> GetCheckTargets(Configuration config, IEnumerable<ScriptEntry> entries) =>
        [new("claudex", null, config.ClaudexUrl, CurrentVersion, config.Schedule ?? CheckSchedule.Default),
        .. entries.Where(e => e.Url is not null && e.Version is not null && e.Error is null)
            .Select(e => new CheckTarget("script:" + e.Id, e.Id, e.Url!, e.Version!, EffectiveSchedule(config, e.Id)))];
    internal List<CheckTarget> GetCheckTargets()
    {
        var config = ReadConfig();
        var entries = new List<ScriptEntry>();
        foreach (var path in Directory.Exists(Scripts) ? Directory.GetFiles(Scripts, "*.js") : [])
        {
            var id = Path.GetFileNameWithoutExtension(path);
            try
            {
                var metadata = UserscriptMetadata.Parse(File.ReadAllText(path));
                if (metadata.Id != id) continue;
                entries.Add(new(id, metadata.Name, metadata.Version, config.Scripts.GetValueOrDefault(id) ?? metadata.UpdateUrl ?? BundledUrl(id), false, null));
            }
            catch (Exception error) when (error is FormatException or IOException) { /* Inventory reports invalid scripts; do not poll them. */ }
        }
        return GetCheckTargets(config, entries);
    }
    private async Task<object> RunCheckAsync(CheckTarget target, Func<Task<object>> fetch)
    {
        Task<object> task;
        lock (checksGate)
        {
            if (!inFlightChecks.TryGetValue(target.Fingerprint, out task!))
                inFlightChecks[target.Fingerprint] = task = FetchAndCacheAsync(target, fetch);
        }
        try { return await task; }
        finally
        {
            lock (checksGate)
                if (inFlightChecks.GetValueOrDefault(target.Fingerprint) == task) inFlightChecks.Remove(target.Fingerprint);
        }
    }
    private async Task<object> FetchAndCacheAsync(CheckTarget target, Func<Task<object>> fetch)
    {
        object result;
        try { result = await fetch(); }
        catch (Exception error)
        {
            await SaveCheckAsync(target, null, error.Message);
            throw;
        }
        await SaveCheckAsync(target, JsonSerializer.SerializeToElement(result, Json), null);
        return result;
    }
    private async Task SaveCheckAsync(CheckTarget target, JsonElement? result, string? error)
    {
        using var lease = await LockAsync("source-checks.lock");
        var checks = ReadChecks();
        checks[target.Key] = new(target.Url, target.InstalledVersion, DateTime.UtcNow, result, error);
        await WriteAsync(ChecksPath, checks);
    }
}

// One scheduler per watcher, independent of Settings visibility and the number of windows.
internal sealed class SourceCheckScheduler
{
    private readonly ScriptSources sources;
    private readonly Func<ScriptSources.CheckTarget, Task> check;
    private readonly Dictionary<string, Task> running = new();
    private readonly Dictionary<string, (string Policy, DateTime Since)> seen = new();
    private readonly Dictionary<string, DateTime> attempted = new();
    private string? launchId;
    internal SourceCheckScheduler(ScriptSources sources, Func<ScriptSources.CheckTarget, Task>? check = null)
    {
        this.sources = sources;
        this.check = check ?? (async target =>
        {
            if (target.Name is null) await sources.CheckClaudexAsync();
            else await sources.CheckScriptAsync(target.Name);
        });
    }
    internal static bool IsDue(ScriptSources.CheckSchedule schedule, DateTime since, DateTime now, DateTime? lastAttempt)
    {
        if (schedule.OnStartup && (lastAttempt is null || lastAttempt < since)) return true;
        return schedule.IntervalMinutes > 0 && now >= (lastAttempt ?? since).AddMinutes(schedule.IntervalMinutes);
    }
    internal void Tick(string currentLaunchId, DateTime now)
    {
        if (launchId != currentLaunchId) { seen.Clear(); attempted.Clear(); launchId = currentLaunchId; }
        foreach (var key in running.Keys.Where(k => running[k].IsCompleted).ToArray()) running.Remove(key);
        var targets = sources.GetCheckTargets();
        var cached = sources.ReadChecks();
        foreach (var target in targets)
        {
            var policy = $"{target.Schedule.OnStartup}:{target.Schedule.IntervalMinutes}";
            if (!seen.TryGetValue(target.Fingerprint, out var state) || state.Policy != policy)
                seen[target.Fingerprint] = state = (policy, now);
            var last = ScriptSources.MatchingCheck(cached, target)?.CheckedAtUtc;
            if (attempted.TryGetValue(target.Fingerprint, out var localAttempt) && (last is null || localAttempt > last)) last = localAttempt;
            if (running.Count >= 3 || running.ContainsKey(target.Key) || !IsDue(target.Schedule, state.Since, now, last)) continue;
            attempted[target.Fingerprint] = now;
            running[target.Key] = CheckSafelyAsync(target);
        }
        var live = targets.Select(t => t.Fingerprint).ToHashSet();
        foreach (var key in seen.Keys.Where(k => !live.Contains(k)).ToArray()) { seen.Remove(key); attempted.Remove(key); }
    }
    private async Task CheckSafelyAsync(ScriptSources.CheckTarget target)
    {
        try { await check(target); }
        catch (Exception error) { Console.Error.WriteLine($"Update check for {target.Key}: {error.Message}"); }
    }
    internal Task DrainAsync() => Task.WhenAll(running.Values);
}

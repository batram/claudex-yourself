using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClaudexYourself;

// Downloads are data until a reviewed, expiring preview is explicitly installed.
internal sealed partial class ScriptSources
{
    internal const string DefaultManifest = "https://raw.githubusercontent.com/batram/claudex-yourself/master/claudex-release.json";
    internal static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    internal static string CurrentVersion => typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    internal static readonly ScriptSources Default = new(Program.StateDirectory);
    private readonly string root;
    private readonly HttpClient client;
    private readonly Func<string> codexVersion;
    private string Scripts => Path.Combine(root, "scripts");
    private string ConfigPath => Path.Combine(root, "sources.json");
    internal ScriptSources(string root, HttpClient? client = null, Func<string>? codexVersion = null)
    {
        this.root = root;
        this.client = client ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        this.codexVersion = codexVersion ?? UserscriptMetadata.CurrentCodexVersion;
    }
    internal sealed record Configuration(string ClaudexUrl, Dictionary<string, string> Scripts, CheckSchedule? Schedule = null, Dictionary<string, CheckSchedule>? ScriptSchedules = null);
    internal sealed record Preview(string PreviewId, string Url, UserscriptMetadata Metadata, string Source, string Sha256,
        string? InstalledVersion, string? InstalledSha256, UserscriptMetadata.CompatibilityResult Compatibility, DateTime ExpiresAtUtc);
    internal sealed record Release(string Version, string DownloadUrl);
    internal sealed record ScriptEntry(string Id, string Name, string? Version, string? Url, bool Autoload, string? Error);
    private Configuration ReadConfig() => File.Exists(ConfigPath)
        ? JsonSerializer.Deserialize<Configuration>(File.ReadAllText(ConfigPath), Json) ?? throw new FormatException("Invalid sources configuration.")
        : new(DefaultManifest, new(StringComparer.OrdinalIgnoreCase));
    internal static string Hash(string source) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    private static string? BundledUrl(string id) => id is "userscript_settings" or "sidebar_usage" or "hide_pets_button" or "hide_invite_a_friend" or "codex_updates"
        ? "https://raw.githubusercontent.com/batram/claudex-yourself/master/scripts/" + id + ".js" : null;
    internal static Uri NormalizeUrl(string url)
    {
        if (url.Length > 4096 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
            throw new FormatException("Use an HTTP or HTTPS URL without embedded credentials or a fragment.");
        // Accept GitHub's file links as well as raw URLs. Branches containing slashes should use the Raw link.
        if (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5 && parts[2] == "blob")
                uri = new Uri("https://raw.githubusercontent.com/" + string.Join('/', parts.Take(2).Concat(parts.Skip(3))));
        }
        return uri;
    }
    internal async Task<string> DownloadAsync(string url)
    {
        var uri = NormalizeUrl(url);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        for (var redirects = 0; redirects <= 5; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("Claudex-Yourself/" + CurrentVersion);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var next = NormalizeUrl(new Uri(uri, response.Headers.Location ?? throw new IOException("Redirect has no destination.")).AbsoluteUri);
                if (uri.Scheme == "https" && next.Scheme != "https") throw new IOException("Refusing an HTTPS to HTTP redirect.");
                uri = next;
                continue;
            }
            response.EnsureSuccessStatusCode();
            const int maxBytes = 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxBytes) throw new IOException("Source exceeds the 1 MiB limit.");
            using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(buffer, timeout.Token)) > 0)
            {
                if (output.Length + count > maxBytes) throw new IOException("Source exceeds the 1 MiB limit.");
                output.Write(buffer, 0, count);
            }
            return new UTF8Encoding(false, true).GetString(output.ToArray()).TrimStart('\uFEFF');
        }
        throw new IOException("Too many source redirects.");
    }
    internal static int CompareVersions(string left, string right)
    {
        static (BigInteger[] Core, string[] Pre) Parse(string value)
        {
            var match = Regex.Match(value, @"^v?(\d+(?:\.\d+){0,3})(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]+)?$");
            if (!match.Success || value.Length > 128) throw new FormatException($"Unsupported version '{value}'. Use a dotted numeric version, optionally with a prerelease suffix.");
            return (match.Groups[1].Value.Split('.').Select(BigInteger.Parse).ToArray(), match.Groups[2].Success ? match.Groups[2].Value.Split('.') : []);
        }
        var a = Parse(left); var b = Parse(right);
        for (var i = 0; i < Math.Max(a.Core.Length, b.Core.Length); i++)
        {
            var order = (i < a.Core.Length ? a.Core[i] : 0).CompareTo(i < b.Core.Length ? b.Core[i] : 0);
            if (order != 0) return order;
        }
        if (a.Pre.Length == 0 || b.Pre.Length == 0) return (a.Pre.Length == 0 ? 1 : 0).CompareTo(b.Pre.Length == 0 ? 1 : 0);
        for (var i = 0; i < Math.Min(a.Pre.Length, b.Pre.Length); i++)
        {
            var an = BigInteger.TryParse(a.Pre[i], out var av); var bn = BigInteger.TryParse(b.Pre[i], out var bv);
            var order = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(a.Pre[i], b.Pre[i]);
            if (order != 0) return order;
        }
        return a.Pre.Length.CompareTo(b.Pre.Length);
    }
    private static async Task WriteAsync(string path, object value) => await WriteTextAsync(path, JsonSerializer.Serialize(value, Json));
    private static async Task WriteTextAsync(string path, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllTextAsync(temporary, source); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private async Task<FileStream> LockAsync(string file = "sources.lock")
    {
        Directory.CreateDirectory(root);
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(root, file), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100); }
        }
    }
    internal async Task<object> SetSourceAsync(string? name, string url)
    {
        var normalized = NormalizeUrl(url).AbsoluteUri;
        using var lease = await LockAsync();
        var config = ReadConfig();
        if (name is null) config = config with { ClaudexUrl = normalized };
        else
        {
            name = UserscriptMetadata.NormalizeId(name);
            if (!File.Exists(Path.Combine(Scripts, name + ".js"))) throw new FileNotFoundException("Install the script before changing its update source.");
            config.Scripts[name] = normalized;
        }
        await WriteAsync(ConfigPath, config);
        return new { saved = true, name, url = normalized };
    }
    internal object Snapshot()
    {
        var config = ReadConfig();
        var entries = new List<ScriptEntry>();
        foreach (var path in Directory.Exists(Scripts) ? Directory.GetFiles(Scripts, "*.js").Order().ToArray() : [])
        {
            var id = Path.GetFileNameWithoutExtension(path);
            try
            {
                var metadata = UserscriptMetadata.Parse(File.ReadAllText(path));
                var url = config.Scripts.GetValueOrDefault(id) ?? metadata.UpdateUrl ?? BundledUrl(id);
                entries.Add(new(id, metadata.Name, metadata.Version, url, AutoloadNames().Contains(id), null));
            }
            catch (Exception error) { entries.Add(new(id, id, null, config.Scripts.GetValueOrDefault(id), false, error.Message)); }
        }
        var lastPath = Path.Combine(root, "sources-last-install.json");
        var checks = ReadChecks();
        var targets = GetCheckTargets(config, entries);
        return new { version = CurrentVersion, claudexUrl = config.ClaudexUrl, defaultSchedule = config.Schedule ?? CheckSchedule.Default,
            claudexCheck = MatchingCheck(checks, targets[0]),
            scripts = entries.Select(entry => new { entry.Id, entry.Name, entry.Version, entry.Url, entry.Autoload, entry.Error,
                schedule = EffectiveSchedule(config, entry.Id), scheduleInherited = config.ScriptSchedules?.ContainsKey(entry.Id) != true,
                check = targets.FirstOrDefault(t => t.Name == entry.Id) is { } target ? MatchingCheck(checks, target) : null }),
            lastInstall = File.Exists(lastPath) ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(lastPath)) : (JsonElement?)null };
    }
    private HashSet<string> AutoloadNames() => File.Exists(Path.Combine(root, "autoload.json"))
        ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(Path.Combine(root, "autoload.json"))) ?? [] : [];
    internal async Task<object> CheckClaudexAsync(string? url = null)
    {
        url = NormalizeUrl(url ?? ReadConfig().ClaudexUrl).AbsoluteUri;
        var target = new CheckTarget("claudex", null, url, CurrentVersion, ReadConfig().Schedule ?? CheckSchedule.Default);
        return await RunCheckAsync(target, async () =>
        {
            var release = JsonSerializer.Deserialize<Release>(await DownloadAsync(url), Json) ?? throw new FormatException("Release manifest is empty.");
            if (string.IsNullOrWhiteSpace(release.Version) || string.IsNullOrWhiteSpace(release.DownloadUrl)) throw new FormatException("Release manifest requires version and downloadUrl.");
            return new { installedVersion = CurrentVersion, availableVersion = release.Version,
                updateAvailable = CompareVersions(release.Version, CurrentVersion) > 0, url,
                downloadUrl = NormalizeUrl(release.DownloadUrl).AbsoluteUri, checkedAtUtc = DateTime.UtcNow };
        });
    }
    internal async Task<object> CheckScriptAsync(string name)
    {
        name = UserscriptMetadata.NormalizeId(name);
        var source = await File.ReadAllTextAsync(Path.Combine(Scripts, name + ".js"));
        var installed = UserscriptMetadata.Parse(source);
        var config = ReadConfig();
        var url = config.Scripts.GetValueOrDefault(name) ?? installed.UpdateUrl ?? BundledUrl(name) ?? throw new InvalidOperationException("No update URL configured for this script.");
        var target = new CheckTarget("script:" + name, name, url, installed.Version, EffectiveSchedule(config, name));
        return await RunCheckAsync(target, async () =>
        {
            var remote = UserscriptMetadata.Parse(await DownloadAsync(url));
            if (remote.Id != name) throw new FormatException("The source @id does not match the installed script.");
            return new { name, installedVersion = installed.Version, availableVersion = remote.Version, url,
                updateAvailable = CompareVersions(remote.Version, installed.Version) > 0,
                compatibility = remote.Compatibility(codexVersion()), checkedAtUtc = DateTime.UtcNow };
        });
    }
    internal async Task<Preview> PreviewAsync(string url, string? expectedName = null)
    {
        url = NormalizeUrl(url).AbsoluteUri;
        var source = await DownloadAsync(url);
        var metadata = UserscriptMetadata.Parse(source);
        CompareVersions(metadata.Version, metadata.Version);
        if (expectedName is not null && metadata.Id != UserscriptMetadata.NormalizeId(expectedName)) throw new FormatException("The source @id does not match the requested script.");
        var path = Path.Combine(Scripts, metadata.Id + ".js");
        var installed = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
        var version = installed is null ? null : UserscriptMetadata.Parse(installed).Version;
        if (version is not null && CompareVersions(metadata.Version, version) <= 0) throw new InvalidOperationException("The source must have a newer @version than the installed script. Equal versions and downgrades are not installed.");
        var preview = new Preview(Guid.NewGuid().ToString("N"), url, metadata, source, Hash(source), version,
            installed is null ? null : Hash(installed), metadata.Compatibility(codexVersion()), DateTime.UtcNow.AddMinutes(15));
        using var lease = await LockAsync();
        var directory = Path.Combine(root, "source-previews");
        Directory.CreateDirectory(directory);
        foreach (var old in Directory.GetFiles(directory, "*.json").Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddMinutes(-15))) File.Delete(old);
        await WriteAsync(Path.Combine(directory, preview.PreviewId + ".json"), preview);
        return preview;
    }
    internal async Task<object> InstallAsync(string previewId, bool autoload, bool run,
        Func<string, string, Task<object>>? activate = null)
    {
        if (!Regex.IsMatch(previewId, "^[a-f0-9]{32}$")) throw new FormatException("Invalid preview ID.");
        Preview preview;
        using var lease = await LockAsync();
        {
            var previewPath = Path.Combine(root, "source-previews", previewId + ".json");
            preview = JsonSerializer.Deserialize<Preview>(await File.ReadAllTextAsync(previewPath), Json) ?? throw new FormatException("Invalid preview.");
            if (preview.ExpiresAtUtc < DateTime.UtcNow) throw new InvalidOperationException("Preview expired. Review the URL again.");
            var metadata = UserscriptMetadata.Parse(preview.Source);
            if (Hash(preview.Source) != preview.Sha256 || metadata.Id != preview.Metadata.Id) throw new InvalidOperationException("Preview content changed. Review the URL again.");
            if (!metadata.Compatibility(codexVersion()).PlatformSupported) throw new PlatformNotSupportedException("This script does not support the current platform.");
            var path = Path.Combine(Scripts, metadata.Id + ".js");
            var old = File.Exists(path) ? await File.ReadAllTextAsync(path) : null;
            if ((old is null ? null : Hash(old)) != preview.InstalledSha256) throw new InvalidOperationException("The installed script changed since preview. Review again before replacing it.");
            var config = ReadConfig();
            config.Scripts[metadata.Id] = preview.Url;
            // Keep the previous source available for recovery, including local edits.
            if (old is not null) await WriteTextAsync(Path.Combine(root, "source-backups", metadata.Id + ".js"), old);
            await WriteTextAsync(path, preview.Source);
            await WriteAsync(ConfigPath, config);
            File.Delete(previewPath);
        }
        object? activation = null;
        string? activationError = null;
        if (run)
        {
            try { activation = await (activate ?? ActivateAsync)(preview.Source, preview.Metadata.Id); }
            catch (Exception error) { activationError = error.Message; }
        }
        AutoloadScripts.SetInDirectory(root, preview.Metadata.Id, autoload);
        var result = new { installed = true, name = preview.Metadata.Id, version = preview.Metadata.Version, preview.Url,
            preview.Sha256, autoload, activation, activationError,
            message = run ? activationError is null ? "Saved. See activation results for each open window." : "Saved, but activation failed. The previous source is in source-backups when replacing a script." : "Saved. Run the script or reopen through Claudex to activate it.",
            installedAtUtc = DateTime.UtcNow };
        await WriteAsync(Path.Combine(root, "sources-last-install.json"), result);
        return result;
    }
    private static async Task<object> ActivateAsync(string source, string id)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var targets = JsonDocument.Parse(await DevToolsLoopback.ReadMainTargetListAsync(client));
        var results = new List<object>();
        foreach (var target in targets.RootElement.EnumerateArray().Where(t => t.GetProperty("type").GetString() == "page" && RendererDevTools.IsUserscriptPage(t.GetProperty("url").GetString())))
        {
            var targetId = target.GetProperty("id").GetString();
            try { var result = await Program.RunScriptAsync(source, id, target.GetProperty("webSocketDebuggerUrl").GetString()); results.Add(new { targetId, activated = true, result.Result }); }
            catch (Exception error) { results.Add(new { targetId, activated = false, error = error.Message }); }
        }
        return results;
    }
}

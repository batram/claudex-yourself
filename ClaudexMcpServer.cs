using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;

namespace ClaudexYourself;

internal static class ClaudexMcpServer
{
    private const string ServerName = "claudex-yourself";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> RunAsync()
    {
        while (await Console.In.ReadLineAsync() is { } line)
        {
            line = line.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(line)) continue;
            JsonObject? request = null;
            try
            {
                request = JsonNode.Parse(line)?.AsObject() ?? throw new InvalidOperationException("Request must be a JSON object.");
                var id = request["id"]?.DeepClone();
                var method = request["method"]?.GetValue<string>();
                if (id is null) continue;
                var result = method switch
                {
                    "initialize" => Initialize(request),
                    "ping" => new JsonObject(),
                    "tools/list" => new JsonObject { ["tools"] = JsonSerializer.SerializeToNode(Tools, JsonOptions) },
                    "tools/call" => await CallToolAsync(request["params"]?.AsObject()),
                    _ => throw new McpException(-32601, $"Method not found: {method}")
                };
                await WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result });
            }
            catch (Exception exception)
            {
                var id = request?["id"]?.DeepClone();
                if (id is null) continue;
                var code = exception is McpException mcp ? mcp.Code : -32603;
                await WriteAsync(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = id,
                    ["error"] = new JsonObject { ["code"] = code, ["message"] = exception.Message }
                });
            }
        }
        return 0;
    }

    private static JsonObject Initialize(JsonObject request)
    {
        var requested = request["params"]?["protocolVersion"]?.GetValue<string>();
        var protocol = requested is "2025-06-18" or "2025-03-26" ? requested : "2025-03-26";
        return new JsonObject
        {
            ["protocolVersion"] = protocol,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "0.1.0" },
            ["instructions"] = "Run explicit local Codex Desktop userscripts. Read a script before changing or running it. reload_mcp schedules a detached verified refresh because the current MCP transport is replaced during that operation."
        };
    }

    private static async Task<JsonObject> CallToolAsync(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>() ?? throw new McpException(-32602, "Tool name is required.");
        var arguments = parameters?["arguments"]?.AsObject() ?? new JsonObject();
        try
        {
            object? value = name switch
            {
                "claudex_status" => await StatusAsync(),
                "list_userscripts" => ListScripts(),
                "read_userscript" => await ReadScriptAsync(RequiredName(arguments)),
                "write_userscript" => await WriteScriptAsync(RequiredName(arguments), RequiredString(arguments, "source"), arguments["overwrite"]?.GetValue<bool>() ?? false),
                "run_userscript" => await RunScriptAsync(RequiredName(arguments)),
                "get_userscript_metadata" => await GetMetadataAsync(RequiredName(arguments)),
                "list_userscript_compatibility" => ListCompatibility(),
                "mark_userscript_tested" => await MarkTestedAsync(RequiredName(arguments)),
                "set_userscript_autoload" => SetAutoload(RequiredName(arguments), arguments["enabled"]?.GetValue<bool>() ?? throw new McpException(-32602, "enabled is required.")),
                "get_autoload_status" => await AutoloadScripts.ReadStatusAsync(),
                "inspect_renderer" => await RendererDevTools.InspectAsync(OptionalString(arguments, "selector"), OptionalString(arguments, "text"), arguments["max_results"]?.GetValue<int>() ?? 10),
                "interact_renderer" => await InteractRendererAsync(arguments),
                "capture_renderer" => await RendererDevTools.CaptureScreenshotAsync(OptionalString(arguments, "selector")),
                "evaluate_renderer" => await RendererDevTools.EvaluateForInspectionAsync(RequiredString(arguments, "expression")),
                "get_userscript_runtime_status" => await RuntimeStatusAsync(),
                "search_renderer_sources" => await RendererDevTools.SearchLoadedSourcesAsync(RequiredString(arguments, "query"), arguments["max_results"]?.GetValue<int>() ?? 10),
                "reload_mcp" => ScheduleReload(),
                "get_reload_status" => await ReadReloadStatusAsync(),
                _ => throw new McpException(-32602, $"Unknown tool: {name}")
            };
            var structured = JsonSerializer.SerializeToNode(value, JsonOptions) as JsonObject ?? new JsonObject { ["result"] = JsonSerializer.SerializeToNode(value, JsonOptions) };
            return new JsonObject
            {
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = structured.ToJsonString() }),
                ["structuredContent"] = structured
            };
        }
        catch (Exception exception) when (exception is not McpException)
        {
            return new JsonObject
            {
                ["isError"] = true,
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = exception.Message }),
                ["structuredContent"] = new JsonObject { ["error"] = exception.Message }
            };
        }
    }

    private static async Task<object> StatusAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var targets = await client.GetFromJsonAsync<JsonArray>("http://127.0.0.1:9229/json/list");
        var rendererCount = targets?.Count(node => node?["type"]?.GetValue<string>() == "page") ?? 0;
        return new { controlled = rendererCount > 0, rendererCount, userScriptDirectory = Program.UserScriptDirectory };
    }

    private static object ListScripts()
    {
        Directory.CreateDirectory(Program.UserScriptDirectory);
        var codexVersion = UserscriptMetadata.CurrentCodexVersion();
        return new
        {
            userScriptDirectory = Program.UserScriptDirectory,
            user = ScriptNames(Program.UserScriptDirectory),
            bundled = ScriptNames(Program.BundledScriptDirectory),
            autoload = AutoloadScripts.EnabledNames(),
            currentCodexVersion = codexVersion,
            compatibility = CompatibilityEntries(codexVersion)
        };
    }

    private static string[] ScriptNames(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory, "*.js").Select(path => Path.GetFileNameWithoutExtension(path)!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
        : [];

    private static async Task<object> ReadScriptAsync(string name)
    {
        var path = ExistingUserScriptPath(name);
        var source = await File.ReadAllTextAsync(path);
        var metadata = UserscriptMetadata.Parse(source);
        return new { name = Path.GetFileNameWithoutExtension(path), source, metadata, compatibility = metadata.Compatibility(UserscriptMetadata.CurrentCodexVersion()) };
    }

    private static async Task<object> WriteScriptAsync(string name, string source, bool overwrite)
    {
        Directory.CreateDirectory(Program.UserScriptDirectory);
        var path = UserScriptPath(name);
        if (File.Exists(path) && !overwrite) throw new IOException($"User script '{name}' already exists; pass overwrite=true to replace it.");
        var metadata = UserscriptMetadata.Parse(source);
        if (!metadata.Id.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new FormatException($"Userscript @id '{metadata.Id}' must match filename '{name}'.");
        await File.WriteAllTextAsync(path, source);
        return new { written = true, name = Path.GetFileNameWithoutExtension(path), bytes = new FileInfo(path).Length, metadata };
    }

    private static async Task<object> RunScriptAsync(string name)
    {
        var path = Program.ResolveScript(name);
        var source = await File.ReadAllTextAsync(path);
        var isUserScript = Path.GetDirectoryName(path)?.Equals(Program.UserScriptDirectory, StringComparison.OrdinalIgnoreCase) == true;
        var metadata = isUserScript ? UserscriptMetadata.Parse(source) : null;
        var compatibility = metadata?.Compatibility(UserscriptMetadata.CurrentCodexVersion());
        if (compatibility is { PlatformSupported: false }) throw new PlatformNotSupportedException($"Userscript '{name}' does not support this platform.");
        var result = await Program.RunScriptAsync(source, Path.GetFileNameWithoutExtension(path));
        return new { name = Path.GetFileNameWithoutExtension(path), metadata, compatibility, logs = result.Logs, result = result.Result };
    }

    private static async Task<object> GetMetadataAsync(string name)
    {
        var metadata = UserscriptMetadata.Parse(await File.ReadAllTextAsync(ExistingUserScriptPath(name)));
        return new { metadata, compatibility = metadata.Compatibility(UserscriptMetadata.CurrentCodexVersion()) };
    }

    private static object ListCompatibility()
    {
        var codexVersion = UserscriptMetadata.CurrentCodexVersion();
        return new { currentCodexVersion = codexVersion, scripts = CompatibilityEntries(codexVersion) };
    }

    private static async Task<object> MarkTestedAsync(string name)
    {
        var path = ExistingUserScriptPath(name);
        var codexVersion = UserscriptMetadata.CurrentCodexVersion();
        var source = await File.ReadAllTextAsync(path);
        var updated = UserscriptMetadata.AddTestedVersion(source, codexVersion);
        var changed = source != updated;
        if (changed)
        {
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, updated);
            File.Move(temporary, path, overwrite: true);
        }
        var metadata = UserscriptMetadata.Parse(updated);
        return new { name, changed, currentCodexVersion = codexVersion, metadata, compatibility = metadata.Compatibility(codexVersion) };
    }

    private static object[] CompatibilityEntries(string codexVersion)
    {
        Directory.CreateDirectory(Program.UserScriptDirectory);
        return Directory.EnumerateFiles(Program.UserScriptDirectory, "*.js")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    var metadata = UserscriptMetadata.Parse(File.ReadAllText(path));
                    return (object)new { name, metadata, compatibility = metadata.Compatibility(codexVersion) };
                }
                catch (Exception exception)
                {
                    return new { name, metadata = (object?)null, compatibility = new { status = "metadata_invalid", currentCodexVersion = codexVersion, error = exception.Message } };
                }
            }).ToArray();
    }

    private static object SetAutoload(string name, bool enabled)
    {
        var setting = AutoloadScripts.Set(name, enabled);
        return new { name = setting.Name, enabled = setting.Enabled };
    }

    private static async Task<object> InteractRendererAsync(JsonObject arguments)
    {
        var action = RequiredString(arguments, "action");
        return action switch
        {
            "click" => await RendererDevTools.ClickAsync(OptionalString(arguments, "selector"), OptionalString(arguments, "text")),
            "press_key" => await RendererDevTools.PressKeyAsync(RequiredString(arguments, "key"),
                arguments["modifiers"]?.AsArray().Select(node => node?.GetValue<string>() ?? string.Empty).ToArray() ?? []),
            _ => throw new McpException(-32602, "action must be 'click' or 'press_key'.")
        };
    }

    private static async Task<object> RuntimeStatusAsync()
    {
        const string expression = "(() => { const registry=window[Symbol.for('claudex-yourself.userscript-registry')]; if(!(registry instanceof Map)) return []; return [...registry.entries()].map(([id,controller]) => ({id,installed:Boolean(controller?.installed),reversible:typeof controller?.install==='function'&&typeof controller?.uninstall==='function'})); })()";
        return await RendererDevTools.EvaluateForInspectionAsync(expression);
    }

    private static object ScheduleReload()
    {
        Directory.CreateDirectory(Program.StateDirectory);
        var statusPath = Path.Combine(Program.StateDirectory, "reload-status.json");
        if (File.Exists(statusPath)) File.Delete(statusPath);
        Program.StartReloadWorker();
        return new { scheduled = true, status = "Use get_reload_status after claudex-yourself reconnects." };
    }

    private static async Task<object> ReadReloadStatusAsync()
    {
        var path = Path.Combine(Program.StateDirectory, "reload-status.json");
        if (!File.Exists(path)) return new { state = "pending" };
        return JsonNode.Parse(await File.ReadAllTextAsync(path)) ?? new JsonObject { ["state"] = "invalid" };
    }

    private static string RequiredName(JsonObject arguments) => NormalizeName(RequiredString(arguments, "name"));
    private static string? OptionalString(JsonObject arguments, string property) => arguments[property]?.GetValue<string>() is { Length: > 0 } value ? value : null;
    private static string RequiredString(JsonObject arguments, string property) => arguments[property]?.GetValue<string>() is { Length: > 0 } value
        ? value
        : throw new McpException(-32602, $"{property} is required.");

    private static string NormalizeName(string name)
    {
        var stem = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
        if (stem.Length is 0 or > 80 || stem.Any(character => !(char.IsLetterOrDigit(character) || character is '-' or '_')))
            throw new McpException(-32602, "Script name may contain only letters, digits, '-' and '_'.");
        return stem;
    }

    private static string UserScriptPath(string name) => Path.Combine(Program.UserScriptDirectory, NormalizeName(name) + ".js");
    private static string ExistingUserScriptPath(string name)
    {
        var path = UserScriptPath(name);
        return File.Exists(path) ? path : throw new FileNotFoundException($"User script '{name}' does not exist.");
    }

    private static async Task WriteAsync(JsonObject response)
    {
        await Console.Out.WriteLineAsync(response.ToJsonString());
        await Console.Out.FlushAsync();
    }

    private static readonly object[] Tools =
    [
        Tool("claudex_status", "Check whether the controlled Codex renderer is reachable.", new { }, readOnly: true),
        Tool("list_userscripts", "List explicit per-user scripts and bundled scripts.", new { }, readOnly: true),
        Tool("read_userscript", "Read one per-user JavaScript userscript before changing or running it.", new { name = StringSchema("User script name without a path.") }, ["name"], readOnly: true),
        Tool("write_userscript", "Create or explicitly replace one per-user JavaScript userscript.", new { name = StringSchema("User script name without a path."), source = StringSchema("Async JavaScript function body using the claudex API."), overwrite = new { type = "boolean" } }, ["name", "source"]),
        Tool("run_userscript", "Run one current per-user or bundled userscript by name in the controlled Codex renderer.", new { name = StringSchema("Script name without a path.") }, ["name"]),
        Tool("get_userscript_metadata", "Read parsed metadata and current Codex compatibility for one per-user script.", new { name = StringSchema("User script name without a path.") }, ["name"], readOnly: true),
        Tool("list_userscript_compatibility", "Compare every per-user script's exact @codex-tested entries with the installed Codex version.", new { }, readOnly: true),
        Tool("mark_userscript_tested", "After explicit behavioural confirmation, add the exact installed Codex version to one userscript's metadata header.", new { name = StringSchema("User script name without a path.") }, ["name"]),
        Tool("set_userscript_autoload", "Enable or disable one per-user script during controlled Codex launch.", new { name = StringSchema("User script name without a path."), enabled = new { type = "boolean" } }, ["name", "enabled"]),
        Tool("get_autoload_status", "Read the last controlled-launch autoload worker result.", new { }, readOnly: true),
        Tool("inspect_renderer", "Inspect bounded live Codex renderer DOM matches by CSS selector and/or exact visible text.", new { selector = StringSchema("Optional CSS selector."), text = StringSchema("Optional exact element text."), max_results = new { type = "integer", minimum = 1, maximum = 50, @default = 10 } }, readOnly: true),
        Tool("interact_renderer", "Send trusted CDP input to the controlled renderer. Click a visible selector/text match or press a key with modifiers.", new { action = new { type = "string", @enum = new[] { "click", "press_key" } }, selector = StringSchema("CSS selector for click."), text = StringSchema("Optional exact text filter for click."), key = StringSchema("CDP key value for press_key."), modifiers = new { type = "array", items = new { type = "string", @enum = new[] { "alt", "ctrl", "meta", "shift" } } } }, ["action"]),
        Tool("capture_renderer", "Capture the visible renderer or one CSS-selected element to a local PNG.", new { selector = StringSchema("Optional CSS selector to capture.") }),
        Tool("evaluate_renderer", "Evaluate diagnostic JavaScript in the controlled renderer with serialized bounded output.", new { expression = StringSchema("JavaScript expression, up to 20000 characters.") }, ["expression"]),
        Tool("get_userscript_runtime_status", "Read registered userscript controller installation and reversibility state from the live renderer.", new { }, readOnly: true),
        Tool("search_renderer_sources", "Search currently loaded renderer JavaScript sources and return bounded snippets around exact text matches.", new { query = StringSchema("Literal source text to find."), max_results = new { type = "integer", minimum = 1, maximum = 50, @default = 10 } }, ["query"], readOnly: true),
        Tool("reload_mcp", "Schedule a detached verified Codex MCP reload. This MCP connection is replaced; call get_reload_status after reconnection.", new { }),
        Tool("get_reload_status", "Read the detached MCP reload worker's last status.", new { }, readOnly: true)
    ];

    private static object Tool(string name, string description, object properties, string[]? required = null, bool readOnly = false) => new
    {
        name,
        description,
        inputSchema = new { type = "object", properties, required = required ?? [] },
        annotations = new { readOnlyHint = readOnly, destructiveHint = false, idempotentHint = readOnly }
    };

    private static object StringSchema(string description) => new { type = "string", description };
    private sealed class McpException(int code, string message) : Exception(message) { public int Code { get; } = code; }
}

using System.Net.WebSockets;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudexYourself;

internal static class RendererDevTools
{
    private static readonly Uri TargetListUri = new("http://127.0.0.1:9229/json/list");

    internal static Task WaitForReadyAsync(TimeSpan timeout) => WaitForReadyAsync(async () =>
    {
        try
        {
            return await EvaluateStringAsync("JSON.stringify(location.protocol === 'app:' && document.readyState === 'complete' && Boolean(document.body) && typeof window.electronBridge?.sendMessageFromView === 'function')", TimeSpan.FromSeconds(2)) == "true";
        }
        catch (Exception exception) when (exception is HttpRequestException or WebSocketException or OperationCanceledException or InvalidOperationException)
        {
            return false; // Endpoint creation, page reloads and preload startup can lag activation.
        }
    }, timeout, TimeSpan.FromMilliseconds(250));

    internal static async Task WaitForReadyAsync(Func<Task<bool>> probe, TimeSpan timeout, TimeSpan interval)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (elapsed.Elapsed < timeout)
        {
            if (await probe()) return;
            await Task.Delay(interval);
        }
        throw new TimeoutException("Codex was activated, but its controlled renderer did not become ready. Close Codex and reopen it through claudex-yourself; the restart has not been confirmed.");
    }

    public static async Task<string> EvaluateStringAsync(string expression, TimeSpan timeout)
    {
        await using var session = await Session.ConnectAsync(timeout);
        var remote = await session.EvaluateAsync(expression);
        if (!remote.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Renderer returned no string value: {remote.GetRawText()}");
        return value.GetString()!;
    }

    public static async Task<object> EvaluateForInspectionAsync(string expression, int maxCharacters = 20_000)
    {
        if (string.IsNullOrWhiteSpace(expression) || expression.Length > 20_000)
            throw new ArgumentException("expression must contain between 1 and 20000 characters.");
        var literal = JsonSerializer.Serialize(expression);
        var wrapper = $$"""
            (async () => {
              const value = await (0, eval)({{literal}});
              let serialized;
              try { serialized = JSON.stringify(value === undefined ? null : value); }
              catch (error) { serialized = JSON.stringify({ error: 'Result is not serializable: ' + error.message }); }
              const truncated = serialized.length > {{maxCharacters}};
              return JSON.stringify({ value: truncated ? serialized.slice(0, {{maxCharacters}}) : serialized, truncated });
            })()
            """;
        var envelope = JsonSerializer.Deserialize<JsonElement>(await EvaluateStringAsync(wrapper, TimeSpan.FromSeconds(30)));
        var serialized = envelope.GetProperty("value").GetString()!;
        var truncated = envelope.GetProperty("truncated").GetBoolean();
        return truncated
            ? new { value = (object)serialized, truncated = true }
            : new { value = (object)(JsonNode.Parse(serialized) ?? JsonValue.Create((string?)null)!), truncated = false };
    }

    public static async Task<object> InspectAsync(string? selector, string? text, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(selector) && string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("selector or text is required.");
        maxResults = Math.Clamp(maxResults, 1, 50);
        var selectorLiteral = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(selector) ? "*" : selector);
        var textLiteral = JsonSerializer.Serialize(text?.Trim());
        var expression = $$"""
            (() => {
              const selector = {{selectorLiteral}};
              const wantedText = {{textLiteral}};
              let elements;
              try { elements = [...document.querySelectorAll(selector)]; }
              catch (error) { throw new Error('Invalid selector: ' + error.message); }
              if (wantedText) elements = elements.filter(element => element.textContent?.trim().toLowerCase() === wantedText.toLowerCase());
              return JSON.stringify(elements.slice(0, {{maxResults}}).map(element => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                const ancestors = [];
                for (let current = element.parentElement, depth = 0; current && depth < 4; current = current.parentElement, depth++) {
                  ancestors.push({ tag: current.tagName, id: current.id || null, role: current.getAttribute('role'), ariaLabel: current.getAttribute('aria-label'), className: current.className?.toString().slice(0, 500) || null });
                }
                return {
                  tag: element.tagName, id: element.id || null, role: element.getAttribute('role'),
                  ariaLabel: element.getAttribute('aria-label'), text: element.textContent?.trim().slice(0, 1000) || '',
                  className: element.className?.toString().slice(0, 1000) || null,
                  attributes: Object.fromEntries([...element.attributes].slice(0, 30).map(attribute => [attribute.name, attribute.value.slice(0, 1000)])),
                  rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height },
                  visible: rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth && style.visibility !== 'hidden' && style.display !== 'none',
                  html: element.outerHTML.slice(0, 4000), ancestors
                };
              }));
            })()
            """;
        var json = await EvaluateStringAsync(expression, TimeSpan.FromSeconds(15));
        return new { selector, text, results = JsonSerializer.Deserialize<JsonElement>(json) };
    }

    public static async Task<object> ClickAsync(string? selector, string? text)
    {
        await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(15));
        var selectorLiteral = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(selector) ? "*" : selector);
        var textLiteral = JsonSerializer.Serialize(text?.Trim());
        var expression = $$"""
            (() => {
              let elements;
              try { elements = [...document.querySelectorAll({{selectorLiteral}})]; }
              catch (error) { throw new Error('Invalid selector: ' + error.message); }
              const wantedText = {{textLiteral}};
              if (wantedText) elements = elements.filter(element => element.textContent?.trim().toLowerCase() === wantedText.toLowerCase());
              const element = elements.find(element => {
                const rect = element.getBoundingClientRect();
                return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.right > 0 && rect.top < innerHeight && rect.left < innerWidth;
              });
              if (!element) throw new Error('No visible matching element found.');
              element.scrollIntoView({ block: 'center', inline: 'center' });
              const rect = element.getBoundingClientRect();
              return JSON.stringify({ x: rect.x + rect.width / 2, y: rect.y + rect.height / 2, tag: element.tagName, text: element.textContent?.trim().slice(0, 500) || '', ariaLabel: element.getAttribute('aria-label') });
            })()
            """;
        var target = JsonSerializer.Deserialize<JsonElement>(GetString(await session.EvaluateAsync(expression)));
        var x = target.GetProperty("x").GetDouble();
        var y = target.GetProperty("y").GetDouble();
        await session.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 });
        await session.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 });
        return new { clicked = true, target };
    }

    public static async Task<object> PressKeyAsync(string key, string[] modifiers)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 30) throw new ArgumentException("key is required and must be at most 30 characters.");
        var mask = modifiers.Aggregate(0, (value, modifier) => value | modifier.ToLowerInvariant() switch
        {
            "alt" => 1, "ctrl" or "control" => 2, "meta" or "command" => 4, "shift" => 8,
            _ => throw new ArgumentException($"Unsupported modifier '{modifier}'.")
        });
        await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(15));
        await session.SendAsync("Input.dispatchKeyEvent", new { type = "keyDown", key, modifiers = mask });
        await session.SendAsync("Input.dispatchKeyEvent", new { type = "keyUp", key, modifiers = mask });
        return new { pressed = true, key, modifiers };
    }

    public static async Task<object> CaptureScreenshotAsync(string? selector)
    {
        await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(30));
        object parameters = new { format = "png", captureBeyondViewport = false };
        if (!string.IsNullOrWhiteSpace(selector))
        {
            var literal = JsonSerializer.Serialize(selector);
            var remote = await session.EvaluateAsync($"(() => {{ const element=document.querySelector({literal}); if(!element) throw new Error('Selector not found.'); element.scrollIntoView({{block:'center',inline:'center'}}); const r=element.getBoundingClientRect(); return JSON.stringify({{x:r.x,y:r.y,width:r.width,height:r.height,scale:1}}); }})()");
            var clip = JsonSerializer.Deserialize<JsonElement>(GetString(remote));
            parameters = new { format = "png", captureBeyondViewport = false, clip };
        }
        var result = await session.SendAsync("Page.captureScreenshot", parameters);
        var bytes = Convert.FromBase64String(result.GetProperty("data").GetString()!);
        var directory = Path.Combine(Program.StateDirectory, "captures");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"renderer-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png");
        await File.WriteAllBytesAsync(path, bytes);
        return new { path, bytes = bytes.Length, selector };
    }

    public static async Task<object> SearchLoadedSourcesAsync(string query, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200) throw new ArgumentException("query must contain between 1 and 200 characters.");
        maxResults = Math.Clamp(maxResults, 1, 50);
        await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(60));
        await session.SendAsync("Debugger.enable", new { });
        var scripts = session.Events("Debugger.scriptParsed")
            .Select(item => item.GetProperty("params"))
            .Where(item => item.TryGetProperty("scriptId", out _))
            .Select(item => new { Id = item.GetProperty("scriptId").GetString()!, Url = item.TryGetProperty("url", out var url) ? url.GetString() ?? string.Empty : string.Empty })
            .ToArray();
        var matches = new List<object>();
        foreach (var script in scripts)
        {
            var search = await session.SendAsync("Debugger.searchInContent", new { scriptId = script.Id, query, caseSensitive = false, isRegex = false });
            if (search.GetProperty("result").GetArrayLength() == 0) continue;
            var sourceResult = await session.SendAsync("Debugger.getScriptSource", new { scriptId = script.Id });
            var source = sourceResult.GetProperty("scriptSource").GetString() ?? string.Empty;
            for (var offset = 0; matches.Count < maxResults;)
            {
                var index = source.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (index < 0) break;
                var start = Math.Max(0, index - 500);
                var length = Math.Min(source.Length - start, query.Length + 1000);
                matches.Add(new { url = script.Url, index, snippet = source.Substring(start, length) });
                offset = index + query.Length;
            }
            if (matches.Count >= maxResults) break;
        }
        return new { query, scriptCount = scripts.Length, results = matches };
    }

    private static string GetString(JsonElement remote) => remote.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()!
        : throw new InvalidOperationException($"Renderer returned no string value: {remote.GetRawText()}");

    private sealed class Session : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private readonly CancellationTokenSource _timeout;
        private readonly List<JsonElement> _events = [];
        private int _nextId;

        private Session(TimeSpan timeout) => _timeout = new CancellationTokenSource(timeout);

        public static async Task<Session> ConnectAsync(TimeSpan timeout)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var targets = await http.GetFromJsonAsync<List<Target>>(TargetListUri) ?? [];
            var target = targets.FirstOrDefault(item => item.Type.Equals("page", StringComparison.OrdinalIgnoreCase)
                && item.Url.StartsWith("app://", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.WebSocketDebuggerUrl))
                ?? throw new InvalidOperationException("No controlled Codex renderer is available. Start Codex with 'claudex-yourself launch'.");
            var session = new Session(timeout);
            await session._socket.ConnectAsync(new Uri(target.WebSocketDebuggerUrl), session._timeout.Token);
            return session;
        }

        public async Task<JsonElement> EvaluateAsync(string expression)
        {
            var result = await SendAsync("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true });
            if (result.TryGetProperty("exceptionDetails", out var exception))
                throw new InvalidOperationException($"Renderer evaluation failed: {exception.GetRawText()}");
            return result.GetProperty("result").Clone();
        }

        public async Task<JsonElement> SendAsync(string method, object parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var command = JsonSerializer.Serialize(new { id, method, @params = parameters });
            await _socket.SendAsync(Encoding.UTF8.GetBytes(command), WebSocketMessageType.Text, true, _timeout.Token);
            while (true)
            {
                var response = await ReceiveAsync(_timeout.Token);
                using var document = JsonDocument.Parse(response);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var responseId))
                {
                    if (root.TryGetProperty("method", out _)) _events.Add(root.Clone());
                    continue;
                }
                if (responseId.GetInt32() != id) continue;
                if (root.TryGetProperty("error", out var error)) throw new InvalidOperationException($"CDP {method} failed: {error.GetRawText()}");
                return root.GetProperty("result").Clone();
            }
        }

        public IEnumerable<JsonElement> Events(string method) => _events.Where(item => item.GetProperty("method").GetString() == method);

        private async Task<string> ReceiveAsync(CancellationToken cancellationToken)
        {
            using var output = new MemoryStream();
            var buffer = new byte[16 * 1024];
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Codex closed the DevTools connection.");
                output.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) return Encoding.UTF8.GetString(output.ToArray());
            }
        }

        public async ValueTask DisposeAsync()
        {
            _timeout.Dispose();
            _socket.Dispose();
            await ValueTask.CompletedTask;
        }
    }

    private sealed record Target(
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);
}

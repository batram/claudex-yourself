using System.Text.Json;

namespace ClaudexYourself;

internal static class DevToolsLoopback
{
    private static readonly Uri[] TargetListUris =
    [
        new("http://127.0.0.1:9229/json/list"),
        new("http://[::1]:9229/json/list")
    ];
    private static Uri? _lastSuccessfulUri;

    internal static async Task<string> ReadMainTargetListAsync(HttpClient client, CancellationToken cancellationToken = default)
    {
        var first = _lastSuccessfulUri ?? TargetListUris[0];
        var second = first == TargetListUris[0] ? TargetListUris[1] : TargetListUris[0];
        using var probes = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Race the two loopback families. A refused or stalled address must not
        // hold up the one Chromium actually listens on, especially during startup.
        var pending = new List<Task<(Uri Uri, string? Json)>> { ProbeAsync(first), ProbeAsync(second) };
        while (pending.Count > 0)
        {
            var completed = await Task.WhenAny(pending);
            pending.Remove(completed);
            var result = await completed;
            if (result.Json is not null)
            {
                _lastSuccessfulUri = result.Uri;
                probes.Cancel();
                return result.Json;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException("No main Codex renderer is available at the DevTools endpoint on either loopback address.");

        async Task<(Uri, string?)> ProbeAsync(Uri uri)
        {
            try
            {
                var json = await client.GetStringAsync(uri, probes.Token);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return (uri, null);
                foreach (var target in document.RootElement.EnumerateArray())
                {
                    if (target.ValueKind == JsonValueKind.Object
                        && target.TryGetProperty("type", out var type)
                        && type.ValueKind == JsonValueKind.String
                        && type.GetString()?.Equals("page", StringComparison.OrdinalIgnoreCase) == true
                        && target.TryGetProperty("url", out var url)
                        && url.ValueKind == JsonValueKind.String
                        && RendererDevTools.IsUserscriptPage(url.GetString()))
                    {
                        return (uri, json);
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
            {
                // The other loopback family may be the one Chromium bound.
            }
            return (uri, null);
        }
    }
}

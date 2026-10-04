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

    internal static async Task<string> ReadMainTargetListAsync(HttpClient client)
    {
        var first = _lastSuccessfulUri ?? TargetListUris[0];
        var second = first == TargetListUris[0] ? TargetListUris[1] : TargetListUris[0];
        foreach (var uri in new[] { first, second })
        {
            try
            {
                var json = await client.GetStringAsync(uri);
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) continue;
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
                        _lastSuccessfulUri = uri;
                        return json;
                    }
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
            {
                // The other loopback family may be the one Chromium bound.
            }
        }
        throw new InvalidOperationException("No main Codex renderer is available at the DevTools endpoint on either loopback address.");
    }
}

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Sensei.Engine.Eval;

/// <summary>List prices of the model, in USD per token, from the provider's public model list.</summary>
internal sealed record ListPrice(double Prompt, double Completion);

/// <summary>
/// What a run cost. The provider reports <c>usage.cost</c> on each response (read by <see cref="TimingChatClient"/>);
/// the list price of the model gives an estimate when it does not; and the key's own spend counter, read before and
/// after the run, is the cross-check (it also counts anyone else using the same key, so it is an upper bound).
/// </summary>
internal static class ProviderCost
{
    public static async Task<ListPrice?> ListPriceAsync(string baseUrl, string model, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var stream = await http.GetStreamAsync(baseUrl.TrimEnd('/') + "/models", ct).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            foreach (var entry in document.RootElement.GetProperty("data").EnumerateArray())
            {
                if (entry.GetProperty("id").GetString() != model) continue;
                var pricing = entry.GetProperty("pricing");
                return new ListPrice(Parse(pricing, "prompt"), Parse(pricing, "completion"));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException or InvalidOperationException)
        {
        }
        return null;
    }

    /// <summary>The key's cumulative spend in USD (OpenRouter <c>/key</c>), or null when unreadable. The key is sent only to the provider's own host.</summary>
    public static async Task<double?> KeySpendAsync(string baseUrl, string apiKey, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/key");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            return document.RootElement.GetProperty("data").GetProperty("usage").GetDouble();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private static double Parse(JsonElement pricing, string name) =>
        double.Parse(pricing.GetProperty(name).GetString() ?? "0", CultureInfo.InvariantCulture);
}

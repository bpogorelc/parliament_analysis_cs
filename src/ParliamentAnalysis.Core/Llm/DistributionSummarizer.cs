using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core.Config;

namespace ParliamentAnalysis.Core.Llm;

public sealed class DistributionSummarizer
{
    private readonly AppSettings _settings;
    private readonly IHttpClientFactory? _httpClientFactory;

    public DistributionSummarizer(IOptions<AppSettings> options, IHttpClientFactory? httpClientFactory = null)
    {
        _settings = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string> SummarizeAsync(
        JsonObject distribution,
        string language = "en",
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_settings.GroqApiKey))
        {
            try
            {
                return await SummarizeWithGroqAsync(distribution, language, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var fallback = FallbackSummary(distribution, language);
                return $"{fallback}\n\n(Note: LLM summary failed: {ex.Message})";
            }
        }

        return FallbackSummary(distribution, language);
    }

    private async Task<string> SummarizeWithGroqAsync(
        JsonObject distribution,
        string language,
        CancellationToken cancellationToken)
    {
        using var client = CreateGroqClient();
        var langInstruction = language.StartsWith("de", StringComparison.OrdinalIgnoreCase)
            ? "Write the summary in German."
            : "Write the summary in English.";

        var payload = new
        {
            model = _settings.GroqModel,
            temperature = 0.2,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content =
                        "You summarize German Bundestag parliamentary group (Fraktion) "
                        + "distributions. Use only the provided JSON. Be concise, neutral, "
                        + "and mention data-quality caveats if present.",
                },
                new
                {
                    role = "user",
                    content = $"{langInstruction}\n\nDistribution JSON:\n{distribution.ToJsonString()}",
                },
            },
        };

        using var response = await client.PostAsJsonAsync("chat/completions", payload, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()
            ?.Trim() ?? "";
    }

    private HttpClient CreateGroqClient()
    {
        var client = _httpClientFactory?.CreateClient("groq") ?? new HttpClient();
        if (_httpClientFactory is null)
        {
            client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.GroqApiKey);
        return client;
    }

    internal static string FallbackSummary(JsonObject distribution, string language)
    {
        var period = distribution["wahlperiode"]?.GetValue<int>();
        var total = distribution["total_persons"]?.GetValue<int>() ?? 0;
        var groups = distribution["groups"] as JsonArray ?? [];
        var lines = new List<string>();

        if (language.StartsWith("de", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"In der {period}. Wahlperiode wurden {total} Personen den Fraktionen zugeordnet.");
            foreach (var group in groups.OfType<JsonObject>())
            {
                lines.Add($"- {group["fraktion"]}: {group["count"]} ({group["percentage"]}%)");
            }
        }
        else
        {
            lines.Add($"In legislative period {period}, {total} persons were assigned to parliamentary groups.");
            foreach (var group in groups.OfType<JsonObject>())
            {
                lines.Add($"- {group["fraktion"]}: {group["count"]} ({group["percentage"]}%)");
            }
        }

        var notes = distribution["notes"] as JsonArray;
        if (notes is { Count: > 0 })
        {
            var label = language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "Hinweise" : "Notes";
            lines.Add($"{label}: {notes[0]}");
        }

        return string.Join("\n", lines);
    }
}

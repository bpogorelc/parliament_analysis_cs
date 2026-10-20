using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using ParliamentAnalysis.Core.Tools;

namespace ParliamentAnalysis.Core.Mcp;

public static class McpToolCatalog
{
    public static McpServerTool[] CreateTools(ParliamentTools tools) =>
    [
        McpServerTool.Create(
            async (string query, int wahlperiode = 20, CancellationToken cancellationToken = default) =>
            {
                var result = await tools.GetPersonInfoAsync(query, wahlperiode, cancellationToken)
                    .ConfigureAwait(false);
                return ToDictionary(result);
            },
            new McpServerToolCreateOptions
            {
                Name = "get_person_info",
                Description =
                    "Look up a politician by name or DIP person id and return identity, "
                    + "roles, and parliamentary group (Fraktion). Use for questions like "
                    + "'Who is Friedrich Merz?'.",
            }),
        McpServerTool.Create(
            async (int wahlperiode = 20, bool include_unknown = true, CancellationToken cancellationToken = default) =>
            {
                var result = await tools.GetFraktionDistributionAsync(wahlperiode, include_unknown, cancellationToken)
                    .ConfigureAwait(false);
                return ToDictionary(result);
            },
            new McpServerToolCreateOptions
            {
                Name = "get_fraktion_distribution",
                Description =
                    "Compute parliamentary group (Fraktion) percentage shares for politicians "
                    + "in a legislative period. Use for questions like 'What is the parliamentary "
                    + "group distribution in the 20th legislative period?'.",
            }),
        McpServerTool.Create(
            async (int wahlperiode = 20, string language = "en", CancellationToken cancellationToken = default) =>
            {
                var result = await tools.SummarizeFraktionDistributionAsync(wahlperiode, language, cancellationToken)
                    .ConfigureAwait(false);
                return ToDictionary(result);
            },
            new McpServerToolCreateOptions
            {
                Name = "summarize_fraktion_distribution",
                Description =
                    "Fetch Fraktion distribution for a Wahlperiode and return a natural-language "
                    + "summary plus the underlying numbers.",
            }),
    ];

    public static Dictionary<string, object?> ToDictionary(JsonObject node)
    {
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return (Dictionary<string, object?>)ConvertElement(doc.RootElement)!;
    }

    private static object? ConvertElement(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertElement(p.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertElement).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString(),
        };
}

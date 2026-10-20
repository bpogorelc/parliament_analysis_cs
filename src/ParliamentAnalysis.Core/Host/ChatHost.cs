using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Mcp;
using ParliamentAnalysis.Core.Tools;

namespace ParliamentAnalysis.Core.Host;

public sealed record ChatResult(string Answer, string Path, string Engine, IReadOnlyList<string> ToolsCalled);

public sealed class ChatHost
{
    private readonly AppSettings _settings;
    private readonly ParliamentTools _tools;
    private readonly McpSessionFactory _mcpSessionFactory;
    private readonly IHttpClientFactory? _httpClientFactory;

    private static readonly JsonArray ToolSpecs = new()
    {
        JsonNode.Parse("""
            {"type":"function","function":{"name":"get_person_info","description":"Look up a politician by name or id.","parameters":{"type":"object","properties":{"query":{"type":"string"},"wahlperiode":{"type":"integer","default":20}},"required":["query"]}}}
            """)!,
        JsonNode.Parse("""
            {"type":"function","function":{"name":"get_fraktion_distribution","description":"Compute Fraktion distribution for a Wahlperiode.","parameters":{"type":"object","properties":{"wahlperiode":{"type":"integer","default":20},"include_unknown":{"type":"boolean","default":true}}}}}
            """)!,
        JsonNode.Parse("""
            {"type":"function","function":{"name":"summarize_fraktion_distribution","description":"Distribution plus natural-language summary.","parameters":{"type":"object","properties":{"wahlperiode":{"type":"integer","default":20},"language":{"type":"string","enum":["en","de"],"default":"en"}}}}}
            """)!,
    };

    public ChatHost(
        IOptions<AppSettings> options,
        ParliamentTools tools,
        McpSessionFactory mcpSessionFactory,
        IHttpClientFactory? httpClientFactory = null)
    {
        _settings = options.Value;
        _tools = tools;
        _mcpSessionFactory = mcpSessionFactory;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<ChatResult> AnswerQuestionAsync(
        string question,
        string mode = "direct",
        CancellationToken cancellationToken = default)
    {
        var path = string.Equals(mode, "mcp", StringComparison.OrdinalIgnoreCase) ? "mcp" : "direct";

        if (!string.IsNullOrWhiteSpace(_settings.GroqApiKey))
        {
            try
            {
                if (path == "mcp")
                {
                    await using var session = await _mcpSessionFactory.CreateAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var (answer, tools) = await AnswerWithGroqAsync(
                        question,
                        async (name, args, ct) => await session.CallToolAsync(name, args, ct).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false);
                    return new ChatResult(answer, "mcp", "groq", tools);
                }

                {
                    var (answer, tools) = await AnswerWithGroqAsync(
                        question,
                        async (name, args, ct) => await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false);
                    return new ChatResult(answer, "direct", "groq", tools);
                }
            }
            catch (Exception ex)
            {
                var routed = await AnswerWithRouterAsync(
                    question,
                    async (name, args, ct) => await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false),
                    "direct",
                    cancellationToken).ConfigureAwait(false);
                return routed with
                {
                    Answer = routed.Answer + $"\n\n(Note: tool-calling via {path} failed: {ex.Message})",
                    Engine = "router-fallback",
                };
            }
        }

        if (path == "mcp")
        {
            try
            {
                await using var session = await _mcpSessionFactory.CreateAsync(cancellationToken)
                    .ConfigureAwait(false);
                return await AnswerWithRouterAsync(
                    question,
                    async (name, args, ct) => await session.CallToolAsync(name, args, ct).ConfigureAwait(false),
                    "mcp",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var routed = await AnswerWithRouterAsync(
                    question,
                    async (name, args, ct) => await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false),
                    "direct",
                    cancellationToken).ConfigureAwait(false);
                return routed with
                {
                    Answer = routed.Answer + $"\n\n(Note: MCP path failed: {ex.Message})",
                    Engine = "router-fallback",
                };
            }
        }

        return await AnswerWithRouterAsync(
            question,
            async (name, args, ct) => await _tools.InvokeAsync(name, args, ct).ConfigureAwait(false),
            "direct",
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ChatResult> AnswerWithRouterAsync(
        string question,
        Func<string, JsonObject, CancellationToken, Task<JsonObject>> executor,
        string path,
        CancellationToken cancellationToken)
    {
        var periodMatch = Regex.Match(
            question,
            @"(\d{1,2})\s*(?:th|st|nd|rd)?\s*(?:legislative|wahlperiode)?",
            RegexOptions.IgnoreCase);
        var period = periodMatch.Success ? int.Parse(periodMatch.Groups[1].Value) : 20;
        var toolsCalled = new List<string>();

        if (Regex.IsMatch(question, @"\bwho is\b|\bwer ist\b", RegexOptions.IgnoreCase))
        {
            var name = Regex.Replace(question, @"^(who is|wer ist)\s+", "", RegexOptions.IgnoreCase)
                .Trim(' ', '?', '.');
            toolsCalled.Add("get_person_info");
            var result = await executor(
                "get_person_info",
                new JsonObject { ["query"] = name, ["wahlperiode"] = period },
                cancellationToken).ConfigureAwait(false);
            return new ChatResult(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), path, "router", toolsCalled);
        }

        if (Regex.IsMatch(question, @"summar|zusammenfass", RegexOptions.IgnoreCase))
        {
            var language = Regex.IsMatch(question, @"deutsch|german|\bde\b", RegexOptions.IgnoreCase)
                ? "de"
                : "en";
            toolsCalled.Add("summarize_fraktion_distribution");
            var result = await executor(
                "summarize_fraktion_distribution",
                new JsonObject { ["wahlperiode"] = period, ["language"] = language },
                cancellationToken).ConfigureAwait(false);
            var answer = result["summary"]?.GetValue<string>()
                         ?? result.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            return new ChatResult(answer, path, "router", toolsCalled);
        }

        if (Regex.IsMatch(question, @"distribution|fraktion|parliamentary group|anteil", RegexOptions.IgnoreCase))
        {
            toolsCalled.Add("get_fraktion_distribution");
            var result = await executor(
                "get_fraktion_distribution",
                new JsonObject { ["wahlperiode"] = period },
                cancellationToken).ConfigureAwait(false);
            return new ChatResult(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), path, "router", toolsCalled);
        }

        toolsCalled.Add("get_person_info");
        var fallback = await executor(
            "get_person_info",
            new JsonObject { ["query"] = question, ["wahlperiode"] = period },
            cancellationToken).ConfigureAwait(false);
        return new ChatResult(fallback.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), path, "router", toolsCalled);
    }

    private async Task<(string Answer, List<string> Tools)> AnswerWithGroqAsync(
        string question,
        Func<string, JsonObject, CancellationToken, Task<JsonObject>> executor,
        CancellationToken cancellationToken)
    {
        using var client = CreateGroqClient();
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] =
                    "You are an assistant for German Bundestag DIP data. "
                    + "Always use tools for factual answers. Do not invent Fraktion shares.",
            },
            new JsonObject { ["role"] = "user", ["content"] = question },
        };
        var toolsCalled = new List<string>();

        for (var round = 0; round < 4; round++)
        {
            var payload = new JsonObject
            {
                ["model"] = _settings.GroqModel,
                ["messages"] = messages.DeepClone(),
                ["tools"] = ToolSpecs.DeepClone(),
                ["tool_choice"] = "auto",
                ["temperature"] = 0.1,
            };

            using var response = await client.PostAsJsonAsync("chat/completions", payload, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var message = doc.RootElement.GetProperty("choices")[0].GetProperty("message");
            if (!message.TryGetProperty("tool_calls", out var toolCalls)
                || toolCalls.ValueKind != JsonValueKind.Array
                || toolCalls.GetArrayLength() == 0)
            {
                var content = message.TryGetProperty("content", out var c) ? c.GetString()?.Trim() ?? "" : "";
                return (content, toolsCalled);
            }

            var assistant = new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = message.TryGetProperty("content", out var contentEl)
                    ? contentEl.GetString() ?? ""
                    : "",
                ["tool_calls"] = JsonNode.Parse(toolCalls.GetRawText())!,
            };
            messages.Add(assistant);

            foreach (var call in toolCalls.EnumerateArray())
            {
                var id = call.GetProperty("id").GetString()!;
                var name = call.GetProperty("function").GetProperty("name").GetString()!;
                var argsRaw = call.GetProperty("function").GetProperty("arguments").GetString() ?? "{}";
                var args = JsonNode.Parse(argsRaw) as JsonObject ?? new JsonObject();
                toolsCalled.Add(name);
                var result = await executor(name, args, cancellationToken).ConfigureAwait(false);
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = id,
                    ["content"] = result.ToJsonString(),
                });
            }
        }

        return ("Stopped after too many tool-calling rounds.", toolsCalled);
    }

    private HttpClient CreateGroqClient()
    {
        var client = _httpClientFactory?.CreateClient("groq") ?? new HttpClient
        {
            BaseAddress = new Uri("https://api.groq.com/openai/v1/"),
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.GroqApiKey);
        return client;
    }
}

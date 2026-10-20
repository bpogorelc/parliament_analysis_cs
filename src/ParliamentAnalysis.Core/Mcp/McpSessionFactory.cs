using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Tools;

namespace ParliamentAnalysis.Core.Mcp;

public sealed class McpToolSession : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly IAsyncDisposable? _ownedServer;
    private readonly CancellationTokenSource? _serverCts;

    public McpToolSession(
        McpClient client,
        IAsyncDisposable? ownedServer = null,
        CancellationTokenSource? serverCts = null)
    {
        _client = client;
        _ownedServer = ownedServer;
        _serverCts = serverCts;
    }

    public async Task<JsonObject> CallToolAsync(
        string name,
        JsonObject? arguments,
        CancellationToken cancellationToken = default)
    {
        var args = arguments is null
            ? new Dictionary<string, object?>()
            : McpToolCatalog.ToDictionary(arguments);

        var result = await _client.CallToolAsync(name, args, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ParseToolResult(result);
    }

    public static JsonObject ParseToolResult(CallToolResult result)
    {
        if (result.StructuredContent is JsonElement structured
            && structured.ValueKind == JsonValueKind.Object)
        {
            return JsonNode.Parse(structured.GetRawText()) as JsonObject ?? new JsonObject();
        }

        var texts = new List<string>();
        foreach (var block in result.Content ?? [])
        {
            if (block is TextContentBlock text)
            {
                texts.Add(text.Text);
            }
        }

        if (texts.Count == 0)
        {
            if (result.IsError == true)
            {
                return new JsonObject { ["error"] = "MCP tool returned an error with no content." };
            }

            return new JsonObject();
        }

        try
        {
            var parsed = JsonNode.Parse(texts[0]);
            if (parsed is JsonObject obj)
            {
                return obj;
            }

            return new JsonObject { ["result"] = parsed?.DeepClone() };
        }
        catch (JsonException)
        {
            return new JsonObject { ["result"] = texts[0] };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_serverCts is not null)
        {
            await _serverCts.CancelAsync().ConfigureAwait(false);
        }

        await _client.DisposeAsync().ConfigureAwait(false);
        if (_ownedServer is not null)
        {
            await _ownedServer.DisposeAsync().ConfigureAwait(false);
        }

        _serverCts?.Dispose();
    }
}

public sealed class McpSessionFactory
{
    private readonly ParliamentTools _tools;
    private readonly AppSettings _settings;
    private readonly ILoggerFactory? _loggerFactory;

    public McpSessionFactory(
        ParliamentTools tools,
        IOptions<AppSettings> options,
        ILoggerFactory? loggerFactory = null)
    {
        _tools = tools;
        _settings = options.Value;
        _loggerFactory = loggerFactory;
    }

    public Task<McpToolSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (string.Equals(_settings.McpTransport, "stdio", StringComparison.OrdinalIgnoreCase))
        {
            return CreateStdioSessionAsync(cancellationToken);
        }

        return CreateMemorySessionAsync(cancellationToken);
    }

    private async Task<McpToolSession> CreateMemorySessionAsync(CancellationToken cancellationToken)
    {
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var server = McpServer.Create(
            new StreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream(),
                "parliament-agent-memory",
                _loggerFactory),
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "parliament-agent", Version = "0.2.0" },
                ServerInstructions =
                    "Tools for German Bundestag DIP politician lookup and Fraktion distribution. "
                    + "Prefer get_fraktion_distribution for share questions and get_person_info for "
                    + "individual politicians. Numbers come from tools, not from model memory.",
                ToolCollection = [.. McpToolCatalog.CreateTools(_tools)],
            });

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = server.RunAsync(cts.Token);

        // StreamClientTransport(serverInput, serverOutput): write to server input, read server output.
        var client = await McpClient.CreateAsync(
            new StreamClientTransport(
                clientToServer.Writer.AsStream(),
                serverToClient.Reader.AsStream(),
                _loggerFactory),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new McpToolSession(client, server, cts);
    }

    private async Task<McpToolSession> CreateStdioSessionAsync(CancellationToken cancellationToken)
    {
        var command = Environment.GetEnvironmentVariable("MCP_SERVER_COMMAND");
        var args = Environment.GetEnvironmentVariable("MCP_SERVER_ARGS") ?? "mcp";

        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException(
                "MCP_TRANSPORT=stdio requires MCP_SERVER_COMMAND (e.g. dotnet) and MCP_SERVER_ARGS "
                + "(e.g. run --project src/ParliamentAnalysis.Cli -- mcp). Prefer MCP_TRANSPORT=memory on Render.");
        }

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Command = command,
            Arguments = args.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Name = "parliament-agent-stdio",
        }, _loggerFactory);

        var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new McpToolSession(client);
    }

    public static async Task RunStdioServerAsync(
        ParliamentTools tools,
        CancellationToken cancellationToken = default)
    {
        await using var server = McpServer.Create(
            new StdioServerTransport("parliament-agent"),
            new McpServerOptions
            {
                ServerInfo = new Implementation { Name = "parliament-agent", Version = "0.2.0" },
                ServerInstructions =
                    "Tools for German Bundestag DIP politician lookup and Fraktion distribution. "
                    + "Prefer get_fraktion_distribution for share questions and get_person_info for "
                    + "individual politicians. Numbers come from tools, not from model memory.",
                ToolCollection = [.. McpToolCatalog.CreateTools(tools)],
            });

        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

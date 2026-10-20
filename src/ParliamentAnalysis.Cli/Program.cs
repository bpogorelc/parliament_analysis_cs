using System.Text.Json;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using ParliamentAnalysis.Core;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Host;
using ParliamentAnalysis.Core.Mcp;
using ParliamentAnalysis.Core.Tools;

LoadEnv();

var argsList = args.ToList();
if (argsList.Count == 0)
{
    PrintHelp();
    return 1;
}

var command = argsList[0].ToLowerInvariant();
var settings = AppSettings.FromEnvironment();

await using var provider = DependencyInjection.BuildParliamentServiceProvider(settings);
var tools = provider.GetRequiredService<ParliamentTools>();
var chat = provider.GetRequiredService<ChatHost>();

return command switch
{
    "mcp" => await RunMcpAsync(tools),
    "chat" => await RunChatAsync(chat),
    "distribution" => await RunDistributionAsync(tools, argsList.Skip(1).ToArray()),
    "person" => await RunPersonAsync(tools, argsList.Skip(1).ToArray()),
    "summarize" => await RunSummarizeAsync(tools, argsList.Skip(1).ToArray()),
    _ => PrintHelp(),
};

static async Task<int> RunMcpAsync(ParliamentTools tools)
{
    await McpSessionFactory.RunStdioServerAsync(tools);
    return 0;
}

static async Task<int> RunChatAsync(ChatHost chat)
{
    Console.WriteLine("Parliament agent chat. Empty line or 'exit' to quit.");
    Console.WriteLine("Modes: direct (default) | mcp — prefix with 'mcp:' to force MCP path.");
    while (true)
    {
        Console.Write("You> ");
        string? user;
        try
        {
            user = Console.ReadLine()?.Trim();
        }
        catch (IOException)
        {
            break;
        }

        if (string.IsNullOrEmpty(user) || user.Equals("exit", StringComparison.OrdinalIgnoreCase)
            || user.Equals("quit", StringComparison.OrdinalIgnoreCase))
        {
            break;
        }

        var mode = "direct";
        var question = user;
        if (user.StartsWith("mcp:", StringComparison.OrdinalIgnoreCase))
        {
            mode = "mcp";
            question = user[4..].Trim();
        }

        var result = await chat.AnswerQuestionAsync(question, mode);
        var toolsCalled = result.ToolsCalled.Count > 0 ? string.Join(", ", result.ToolsCalled) : "—";
        Console.WriteLine($"Agent> {result.Answer}");
        Console.WriteLine($"[{result.Engine} · path={result.Path} · tools={toolsCalled}]");
        Console.WriteLine();
    }

    return 0;
}

static async Task<int> RunDistributionAsync(ParliamentTools tools, string[] args)
{
    var period = ParseInt(args, "--wahlperiode", 20);
    var includeUnknown = !args.Contains("--no-unknown");
    var result = await tools.GetFraktionDistributionAsync(period, includeUnknown);
    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static async Task<int> RunPersonAsync(ParliamentTools tools, string[] args)
{
    var query = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "";
    var period = ParseInt(args, "--wahlperiode", 20);
    var result = await tools.GetPersonInfoAsync(query, period);
    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static async Task<int> RunSummarizeAsync(ParliamentTools tools, string[] args)
{
    var period = ParseInt(args, "--wahlperiode", 20);
    var language = ParseString(args, "--language", "en");
    var result = await tools.SummarizeFraktionDistributionAsync(period, language);
    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}

static int ParseInt(string[] args, string name, int fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i + 1], out var value))
        {
            return value;
        }
    }

    return fallback;
}

static string ParseString(string[] args, string name, string fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return fallback;
}

static int PrintHelp()
{
    Console.WriteLine("""
        ParliamentAnalysis.Cli

          mcp
          chat
          distribution [--wahlperiode N] [--no-unknown]
          person <query> [--wahlperiode N]
          summarize [--wahlperiode N] [--language en|de]
        """);
    return 1;
}

static void LoadEnv()
{
    var candidates = new[]
    {
        Path.Combine(Directory.GetCurrentDirectory(), ".env"),
        Path.Combine(AppContext.BaseDirectory, ".env"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".env")),
    };
    foreach (var path in candidates)
    {
        if (File.Exists(path))
        {
            Env.Load(path);
            break;
        }
    }
}

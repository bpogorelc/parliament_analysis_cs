using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotNetEnv;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Host;
using ParliamentAnalysis.Core.Tools;

LoadEnv();

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddParliamentAnalysis(AppSettings.FromEnvironment());

var app = builder.Build();

var wwwroot = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(wwwroot);
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", (IOptions<AppSettings> options) =>
{
    var settings = options.Value;
    return Results.Json(new
    {
        status = "ok",
        dip_configured = !string.IsNullOrWhiteSpace(settings.DipApiKey),
        groq_configured = !string.IsNullOrWhiteSpace(settings.GroqApiKey),
        default_wahlperiode = settings.DefaultWahlperiode,
    });
});

app.MapPost("/api/distribution", async (DistributionRequest body, ParliamentTools tools) =>
{
    try
    {
        var result = await tools.GetFraktionDistributionAsync(body.Wahlperiode, body.IncludeUnknown);
        return Results.Json(ToObject(result));
    }
    catch (Exception)
    {
        return Results.Json(new { detail = "Failed to compute Fraktion distribution from DIP." }, statusCode: 502);
    }
});

app.MapPost("/api/summary", async (SummaryRequest body, ParliamentTools tools) =>
{
    try
    {
        var result = await tools.SummarizeFraktionDistributionAsync(body.Wahlperiode, body.Language);
        return Results.Json(ToObject(result));
    }
    catch (Exception)
    {
        return Results.Json(new { detail = "Failed to generate distribution summary." }, statusCode: 502);
    }
});

app.MapPost("/api/person", async (PersonRequest body, ParliamentTools tools) =>
{
    try
    {
        var result = await tools.GetPersonInfoAsync(body.Query, body.Wahlperiode);
        return Results.Json(ToObject(result));
    }
    catch (Exception)
    {
        return Results.Json(new { detail = "Failed to look up person in DIP." }, statusCode: 502);
    }
});

app.MapPost("/api/chat", async (ChatRequest body, ChatHost chat) =>
{
    try
    {
        var result = await chat.AnswerQuestionAsync(body.Question, body.Mode);
        return Results.Json(new
        {
            answer = result.Answer,
            engine = result.Engine,
            path = result.Path,
            tools_called = result.ToolsCalled,
        });
    }
    catch (Exception)
    {
        return Results.Json(new { detail = "Chat agent failed to answer the question." }, statusCode: 502);
    }
});

app.MapFallbackToFile("index.html");

var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    app.Urls.Clear();
    app.Urls.Add($"http://0.0.0.0:{port}");
}
else if (app.Environment.IsDevelopment())
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:8000");
}

app.Run();

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

static object ToObject(JsonObject node) =>
    JsonSerializer.Deserialize<object>(node.ToJsonString())!;

internal sealed class DistributionRequest
{
    [JsonPropertyName("wahlperiode")]
    public int Wahlperiode { get; set; } = 20;

    [JsonPropertyName("include_unknown")]
    public bool IncludeUnknown { get; set; } = true;
}

internal sealed class SummaryRequest
{
    [JsonPropertyName("wahlperiode")]
    public int Wahlperiode { get; set; } = 20;

    [JsonPropertyName("language")]
    public string Language { get; set; } = "de";
}

internal sealed class PersonRequest
{
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("wahlperiode")]
    public int Wahlperiode { get; set; } = 20;
}

internal sealed class ChatRequest
{
    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "direct";
}

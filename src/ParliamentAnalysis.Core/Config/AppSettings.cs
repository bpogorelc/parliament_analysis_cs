namespace ParliamentAnalysis.Core.Config;

public sealed class AppSettings
{
    public const string SectionName = "Parliament";

    public string DipApiBaseUrl { get; set; } = "https://search.dip.bundestag.de/api/v1";
    public string DipApiKey { get; set; } = "";
    public bool DipSslVerify { get; set; } = true;
    public string GroqApiKey { get; set; } = "";
    public string GroqModel { get; set; } = "openai/gpt-oss-120b";
    public int DefaultWahlperiode { get; set; } = 20;
    public string McpTransport { get; set; } = "memory";

    public static AppSettings FromEnvironment()
    {
        return new AppSettings
        {
            DipApiBaseUrl = Env("DIP_API_BASE_URL", "https://search.dip.bundestag.de/api/v1"),
            DipApiKey = Env("DIP_API_KEY", ""),
            DipSslVerify = AsBool(Environment.GetEnvironmentVariable("DIP_SSL_VERIFY"), true),
            GroqApiKey = Env("GROQ_API_KEY", ""),
            GroqModel = Env("GROQ_MODEL", "openai/gpt-oss-120b"),
            DefaultWahlperiode = int.TryParse(Environment.GetEnvironmentVariable("DEFAULT_WAHLPERIODE"), out var period)
                ? period
                : 20,
            McpTransport = Env("MCP_TRANSPORT", "memory"),
        };
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;

    private static bool AsBool(string? value, bool defaultValue)
    {
        if (value is null)
        {
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }
}

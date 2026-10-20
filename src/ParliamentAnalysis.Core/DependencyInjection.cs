using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParliamentAnalysis.Core.Config;
using ParliamentAnalysis.Core.Dip;
using ParliamentAnalysis.Core.Host;
using ParliamentAnalysis.Core.Llm;
using ParliamentAnalysis.Core.Mcp;
using ParliamentAnalysis.Core.Tools;

namespace ParliamentAnalysis.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddParliamentAnalysis(
        this IServiceCollection services,
        AppSettings? settings = null)
    {
        var resolved = settings ?? AppSettings.FromEnvironment();
        services.AddSingleton(Options.Create(resolved));
        services.AddSingleton(resolved);

        services.AddHttpClient("groq", client =>
        {
            client.BaseAddress = new Uri("https://api.groq.com/openai/v1/");
            client.Timeout = TimeSpan.FromSeconds(90);
        });

        services.AddSingleton<Func<DipClient>>(sp =>
        {
            var appSettings = sp.GetRequiredService<AppSettings>();
            return () => new DipClient(appSettings);
        });

        services.AddSingleton<DistributionSummarizer>();
        services.AddSingleton<ParliamentTools>();
        services.AddSingleton<McpSessionFactory>();
        services.AddSingleton<ChatHost>();

        return services;
    }

    public static ServiceProvider BuildParliamentServiceProvider(AppSettings? settings = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddParliamentAnalysis(settings);
        return services.BuildServiceProvider();
    }
}

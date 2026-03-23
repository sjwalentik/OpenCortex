using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.OpenAI;

/// <summary>
/// Extension methods for registering OpenAI provider services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the OpenAI provider to the service collection.
    /// </summary>
    public static IServiceCollection AddOpenAIProvider(
        this IServiceCollection services,
        Action<OpenAIOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient<OpenAIProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAIProvider));
            var options = sp.GetRequiredService<IOptions<OpenAIOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAIProvider>>();

            return new OpenAIProvider(httpClient, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the OpenAI provider with options from configuration.
    /// </summary>
    public static IServiceCollection AddOpenAIProvider(
        this IServiceCollection services,
        string configurationSection = "OpenCortex:Providers:OpenAI")
    {
        services.AddOptions<OpenAIOptions>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations();

        services.AddHttpClient<OpenAIProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OpenAIProvider));
            var options = sp.GetRequiredService<IOptions<OpenAIOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OpenAIProvider>>();

            return new OpenAIProvider(httpClient, options, logger);
        });

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Anthropic;

/// <summary>
/// Extension methods for registering Anthropic provider services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Anthropic provider to the service collection.
    /// </summary>
    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        Action<AnthropicOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient<AnthropicProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnthropicProvider>>();

            return new AnthropicProvider(httpClient, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the Anthropic provider with options from configuration.
    /// </summary>
    public static IServiceCollection AddAnthropicProvider(
        this IServiceCollection services,
        string configurationSection = "OpenCortex:Providers:Anthropic")
    {
        services.AddOptions<AnthropicOptions>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations();

        services.AddHttpClient<AnthropicProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(AnthropicProvider));
            var options = sp.GetRequiredService<IOptions<AnthropicOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AnthropicProvider>>();

            return new AnthropicProvider(httpClient, options, logger);
        });

        return services;
    }
}

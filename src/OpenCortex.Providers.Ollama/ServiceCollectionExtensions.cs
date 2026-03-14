using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Ollama;

/// <summary>
/// Extension methods for registering Ollama provider services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the Ollama provider to the service collection.
    /// </summary>
    public static IServiceCollection AddOllamaProvider(
        this IServiceCollection services,
        Action<OllamaOptions> configure)
    {
        services.Configure(configure);

        services.AddHttpClient<OllamaProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OllamaProvider));
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaProvider>>();

            return new OllamaProvider(httpClient, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the Ollama provider with options from configuration.
    /// </summary>
    public static IServiceCollection AddOllamaProvider(
        this IServiceCollection services,
        string configurationSection = "OpenCortex:Providers:Ollama")
    {
        services.AddOptions<OllamaOptions>()
            .BindConfiguration(configurationSection)
            .ValidateDataAnnotations();

        services.AddHttpClient<OllamaProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OllamaProvider));
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaProvider>>();

            return new OllamaProvider(httpClient, options, logger);
        });

        return services;
    }

    /// <summary>
    /// Add the Ollama provider with default local configuration.
    /// </summary>
    public static IServiceCollection AddLocalOllamaProvider(this IServiceCollection services)
    {
        var defaults = OllamaOptions.CreateDefault();

        services.AddSingleton(Microsoft.Extensions.Options.Options.Create(defaults));
        services.AddHttpClient<OllamaProvider>();

        services.AddSingleton<IModelProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var httpClient = httpClientFactory.CreateClient(nameof(OllamaProvider));
            var options = sp.GetRequiredService<IOptions<OllamaOptions>>();
            var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<OllamaProvider>>();

            return new OllamaProvider(httpClient, options, logger);
        });

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Orchestration.Configuration;
using OpenCortex.Orchestration.Execution;
using OpenCortex.Orchestration.Routing;

namespace OpenCortex.Orchestration;

/// <summary>
/// Extension methods for registering orchestration services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add orchestration services to the service collection.
    /// </summary>
    public static IServiceCollection AddOrchestration(
        this IServiceCollection services,
        Action<OrchestrationOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<OrchestrationOptions>();
        }

        services.AddSingleton<ITaskClassifier, KeywordTaskClassifier>();
        services.AddSingleton<IModelRouter, DefaultRouter>();
        services.AddSingleton<IOrchestrationEngine, OrchestrationEngine>();

        return services;
    }

    /// <summary>
    /// Add orchestration services with options from configuration.
    /// </summary>
    public static IServiceCollection AddOrchestration(
        this IServiceCollection services,
        string configurationSection)
    {
        services.AddOptions<OrchestrationOptions>()
            .BindConfiguration(configurationSection);

        services.AddSingleton<ITaskClassifier, KeywordTaskClassifier>();
        services.AddSingleton<IModelRouter, DefaultRouter>();
        services.AddSingleton<IOrchestrationEngine, OrchestrationEngine>();

        return services;
    }
}

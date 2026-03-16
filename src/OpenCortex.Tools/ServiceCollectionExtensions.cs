using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCortex.Tools.FileSystem;

namespace OpenCortex.Tools;

/// <summary>
/// Extension methods for registering tool services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add the tool execution infrastructure.
    /// </summary>
    public static IServiceCollection AddTools(this IServiceCollection services)
    {
        services.AddOptions<ToolsOptions>()
            .BindConfiguration(ToolsOptions.SectionName);

        // Register workspace manager based on configuration
        services.AddSingleton<IWorkspaceManager>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ToolsOptions>>();
            return CreateWorkspaceManager(sp, options.Value);
        });

        // Core infrastructure
        services.AddSingleton<IToolExecutor, ToolExecutorRegistry>();

        // Tool definition providers
        services.AddSingleton<IToolDefinitionProvider, FileSystemToolDefinitions>();

        // FileSystem tool handlers
        services.AddSingleton<IToolHandler, ReadFileHandler>();
        services.AddSingleton<IToolHandler, WriteFileHandler>();
        services.AddSingleton<IToolHandler, ListDirectoryHandler>();

        return services;
    }

    /// <summary>
    /// Add the tool execution infrastructure with custom configuration.
    /// </summary>
    public static IServiceCollection AddTools(
        this IServiceCollection services,
        Action<ToolsOptions> configure)
    {
        services.Configure(configure);

        // Register workspace manager based on configuration
        services.AddSingleton<IWorkspaceManager>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ToolsOptions>>();
            return CreateWorkspaceManager(sp, options.Value);
        });

        // Core infrastructure
        services.AddSingleton<IToolExecutor, ToolExecutorRegistry>();

        // Tool definition providers
        services.AddSingleton<IToolDefinitionProvider, FileSystemToolDefinitions>();

        // FileSystem tool handlers
        services.AddSingleton<IToolHandler, ReadFileHandler>();
        services.AddSingleton<IToolHandler, WriteFileHandler>();
        services.AddSingleton<IToolHandler, ListDirectoryHandler>();

        return services;
    }

    private static IWorkspaceManager CreateWorkspaceManager(IServiceProvider sp, ToolsOptions options)
    {
        return options.WorkspaceMode.ToLowerInvariant() switch
        {
            "docker" => ActivatorUtilities.CreateInstance<DockerWorkspaceManager>(sp),
            "kubernetes" or "k8s" => ActivatorUtilities.CreateInstance<KubernetesWorkspaceManager>(sp),
            _ => ActivatorUtilities.CreateInstance<LocalWorkspaceManager>(sp)
        };
    }
}

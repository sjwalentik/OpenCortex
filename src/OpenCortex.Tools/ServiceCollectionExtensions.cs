using Microsoft.Extensions.DependencyInjection;
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

        // Core infrastructure
        services.AddSingleton<IWorkspaceManager, LocalWorkspaceManager>();
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

        // Core infrastructure
        services.AddSingleton<IWorkspaceManager, LocalWorkspaceManager>();
        services.AddSingleton<IToolExecutor, ToolExecutorRegistry>();

        // Tool definition providers
        services.AddSingleton<IToolDefinitionProvider, FileSystemToolDefinitions>();

        // FileSystem tool handlers
        services.AddSingleton<IToolHandler, ReadFileHandler>();
        services.AddSingleton<IToolHandler, WriteFileHandler>();
        services.AddSingleton<IToolHandler, ListDirectoryHandler>();

        return services;
    }
}

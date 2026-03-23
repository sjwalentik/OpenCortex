using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Tools.GitHub.Handlers;

namespace OpenCortex.Tools.GitHub;

/// <summary>
/// Extension methods for registering GitHub tool services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add GitHub tool handlers and client.
    /// </summary>
    public static IServiceCollection AddGitHubTools(this IServiceCollection services)
    {
        // HTTP client for GitHub API
        services.AddHttpClient<IGitHubClient, HttpGitHubClient>();

        // Tool definition provider
        services.AddSingleton<IToolDefinitionProvider, GitHubToolDefinitions>();

        // Tool handlers
        services.AddSingleton<IToolHandler, GetRepositoryHandler>();
        services.AddSingleton<IToolHandler, ListRepositoryFilesHandler>();
        services.AddSingleton<IToolHandler, GetFileContentHandler>();
        services.AddSingleton<IToolHandler, CreateOrUpdateFileHandler>();
        services.AddSingleton<IToolHandler, ListBranchesHandler>();
        services.AddSingleton<IToolHandler, CreateBranchHandler>();
        services.AddSingleton<IToolHandler, CreatePullRequestHandler>();
        services.AddSingleton<IToolHandler, GetPullRequestHandler>();
        services.AddSingleton<IToolHandler, GitCloneHandler>();
        services.AddSingleton<IToolHandler, GitCheckoutHandler>();

        return services;
    }
}

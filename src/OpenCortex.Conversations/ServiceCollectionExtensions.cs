using Microsoft.Extensions.DependencyInjection;

namespace OpenCortex.Conversations;

/// <summary>
/// Extension methods for registering conversation services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add conversation services to the service collection.
    /// Note: You must also register an IConversationRepository implementation.
    /// </summary>
    public static IServiceCollection AddConversations(this IServiceCollection services)
    {
        services.AddScoped<IConversationService, ConversationService>();
        return services;
    }
}

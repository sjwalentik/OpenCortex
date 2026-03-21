using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Tools.Memory.Handlers;

namespace OpenCortex.Tools.Memory;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMemoryTools(this IServiceCollection services)
    {
        services.AddSingleton<IToolDefinitionProvider, MemoryToolDefinitions>();
        services.AddSingleton<IToolHandler, SaveMemoryHandler>();
        services.AddSingleton<IToolHandler, RecallMemoriesHandler>();
        services.AddSingleton<IToolHandler, ForgetMemoryHandler>();
        return services;
    }
}

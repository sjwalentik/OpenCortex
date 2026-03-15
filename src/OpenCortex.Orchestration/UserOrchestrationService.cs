using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Orchestration.Configuration;
using OpenCortex.Orchestration.Execution;
using OpenCortex.Orchestration.Routing;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration;

/// <summary>
/// Executes orchestration requests against the current user's configured providers.
/// </summary>
public interface IUserOrchestrationService
{
    Task<IReadOnlyList<IModelProvider>> GetProvidersAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IModelProvider?> GetProviderAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken = default);

    Task<RoutingDecision> RouteAsync(
        Guid userId,
        string message,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default);

    Task<OrchestrationResult> ExecuteAsync(
        Guid userId,
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<OrchestrationStreamChunk> StreamAsync(
        Guid userId,
        OrchestrationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default per-user orchestration service backed by user-managed provider configuration.
/// </summary>
public sealed class UserOrchestrationService : IUserOrchestrationService
{
    private readonly IUserProviderFactory _userProviderFactory;
    private readonly ITaskClassifier _classifier;
    private readonly IOptions<OrchestrationOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    public UserOrchestrationService(
        IUserProviderFactory userProviderFactory,
        ITaskClassifier classifier,
        IOptions<OrchestrationOptions> options,
        ILoggerFactory loggerFactory)
    {
        _userProviderFactory = userProviderFactory;
        _classifier = classifier;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    public Task<IModelProvider?> GetProviderAsync(
        Guid userId,
        string providerId,
        CancellationToken cancellationToken = default) =>
        _userProviderFactory.GetProviderForUserAsync(userId, providerId, cancellationToken);

    public Task<IReadOnlyList<IModelProvider>> GetProvidersAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        _userProviderFactory.GetProvidersForUserAsync(userId, cancellationToken);

    public async Task<RoutingDecision> RouteAsync(
        Guid userId,
        string message,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var router = await CreateRouterAsync(userId, cancellationToken);
        return await router.RouteAsync(message, context, cancellationToken);
    }

    public async Task<OrchestrationResult> ExecuteAsync(
        Guid userId,
        OrchestrationRequest request,
        CancellationToken cancellationToken = default)
    {
        var router = await CreateRouterAsync(userId, cancellationToken);
        var engine = CreateEngine(router);
        return await engine.ExecuteAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<OrchestrationStreamChunk> StreamAsync(
        Guid userId,
        OrchestrationRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var router = await CreateRouterAsync(userId, cancellationToken);
        var engine = CreateEngine(router);

        await foreach (var chunk in engine.StreamAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task<IModelRouter> CreateRouterAsync(Guid userId, CancellationToken cancellationToken)
    {
        var providers = await _userProviderFactory.GetProvidersForUserAsync(userId, cancellationToken);
        if (providers.Count == 0)
        {
            throw new InvalidOperationException("No enabled providers are configured for this user. Configure a provider in Settings first.");
        }

        return new DefaultRouter(
            _classifier,
            providers,
            Options.Create(_options.Value),
            _loggerFactory.CreateLogger<DefaultRouter>());
    }

    private IOrchestrationEngine CreateEngine(IModelRouter router)
    {
        return new OrchestrationEngine(
            router,
            Options.Create(_options.Value),
            _loggerFactory.CreateLogger<OrchestrationEngine>());
    }
}

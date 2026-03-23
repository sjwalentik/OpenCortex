using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Orchestration.Configuration;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Orchestration.Routing;

/// <summary>
/// Default router implementation using rules and task classification.
/// </summary>
public sealed class DefaultRouter : IModelRouter
{
    private readonly ITaskClassifier _classifier;
    private readonly IReadOnlyList<IModelProvider> _providers;
    private readonly Dictionary<string, IModelProvider> _providerMap;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<DefaultRouter> _logger;

    public DefaultRouter(
        ITaskClassifier classifier,
        IEnumerable<IModelProvider> providers,
        IOptions<OrchestrationOptions> options,
        ILogger<DefaultRouter> logger)
    {
        _classifier = classifier;
        _providers = providers.ToList();
        _providerMap = _providers.ToDictionary(p => p.ProviderId, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _logger = logger;
    }

    public Task<RoutingDecision> RouteAsync(
        string message,
        RoutingContext? context = null,
        CancellationToken cancellationToken = default)
    {
        // Handle explicit provider request
        if (!string.IsNullOrEmpty(context?.RequestedProviderId))
        {
            if (_providerMap.ContainsKey(context.RequestedProviderId))
            {
                _logger.LogDebug("Using explicitly requested provider: {ProviderId}", context.RequestedProviderId);

                return Task.FromResult(new RoutingDecision
                {
                    ProviderId = context.RequestedProviderId,
                    ModelId = context.RequestedModelId,
                    Classification = new TaskClassification { Category = TaskCategory.General },
                    MatchedRule = null
                });
            }

            _logger.LogWarning("Requested provider {ProviderId} not found, falling back to routing", context.RequestedProviderId);
        }

        // Classify the task
        var classification = _classifier.Classify(message);
        _logger.LogDebug(
            "Task classified as {Category} with confidence {Confidence:F2}",
            classification.Category,
            classification.Confidence);

        // Handle private content
        if (context?.IsPrivate == true || classification.Category == TaskCategory.Private)
        {
            var localProvider = FindLocalProvider();
            if (localProvider is not null)
            {
                return Task.FromResult(new RoutingDecision
                {
                    ProviderId = localProvider.ProviderId,
                    Classification = classification,
                    MatchedRule = null
                });
            }
        }

        // Find matching rule
        var matchedRule = FindMatchingRule(classification);

        string providerId;
        string? modelId = null;
        string? fallbackProviderId = null;

        if (matchedRule is not null)
        {
            providerId = matchedRule.TargetProviderId;
            modelId = matchedRule.TargetModelId;
            fallbackProviderId = matchedRule.FallbackProviderId;

            _logger.LogDebug("Matched routing rule: {RuleName}", matchedRule.Name);
        }
        else
        {
            // Use default provider based on category
            providerId = GetDefaultProviderForCategory(classification.Category);

            _logger.LogDebug("No rule matched, using default provider for {Category}: {ProviderId}",
                classification.Category, providerId);
        }

        // Determine if multi-model should be used
        var useMultiModel = context?.ForceMultiModel == true ||
                           (_options.EnableMultiModel && classification.IsHighStakes);

        List<string>? additionalProviders = null;
        if (useMultiModel)
        {
            additionalProviders = _providers
                .Where(p => p.ProviderId != providerId && p.Capabilities.SupportsChat)
                .Take(2)
                .Select(p => p.ProviderId)
                .ToList();
        }

        return Task.FromResult(new RoutingDecision
        {
            ProviderId = providerId,
            ModelId = modelId,
            FallbackProviderId = fallbackProviderId,
            MatchedRule = matchedRule,
            Classification = classification,
            UseMultiModel = useMultiModel && additionalProviders?.Count > 0,
            AdditionalProviderIds = additionalProviders
        });
    }

    public IModelProvider? GetProvider(string providerId)
    {
        return _providerMap.GetValueOrDefault(providerId);
    }

    public IReadOnlyList<IModelProvider> GetProviders() => _providers;

    private RoutingRule? FindMatchingRule(TaskClassification classification)
    {
        return _options.RoutingRules?
            .Where(r => r.IsEnabled)
            .Where(r => r.Category is null || r.Category == classification.Category)
            .OrderBy(r => r.Priority)
            .FirstOrDefault(r => _providerMap.ContainsKey(r.TargetProviderId));
    }

    private string GetDefaultProviderForCategory(TaskCategory category)
    {
        // Map categories to an ordered list of preferred provider types.
        string[] preferredTypes = category switch
        {
            TaskCategory.Code => ["openai-codex", "openai"],
            TaskCategory.Planning => ["anthropic"],
            TaskCategory.Writing => ["anthropic"],
            TaskCategory.Analysis => ["anthropic"],
            TaskCategory.Quick => ["ollama"],
            TaskCategory.Private => ["ollama"],
            TaskCategory.Reasoning => ["anthropic"],
            _ => !string.IsNullOrEmpty(_options.DefaultProvider)
                ? [_options.DefaultProvider]
                : ["anthropic"]
        };

        var provider = preferredTypes
            .Select(preferredType => _providers.FirstOrDefault(p =>
                p.ProviderType.Equals(preferredType, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(candidate => candidate is not null);

        // Fall back to configured default
        if (provider is null && !string.IsNullOrEmpty(_options.DefaultProvider))
        {
            _providerMap.TryGetValue(_options.DefaultProvider, out provider);
        }

        // Fall back to any available provider
        provider ??= _providers.FirstOrDefault();

        return provider?.ProviderId ?? "unknown";
    }

    private IModelProvider? FindLocalProvider()
    {
        return _providers.FirstOrDefault(p =>
            p.ProviderType.Equals("ollama", StringComparison.OrdinalIgnoreCase));
    }
}

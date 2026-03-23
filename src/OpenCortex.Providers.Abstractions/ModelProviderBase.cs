using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Base class for model provider implementations.
/// </summary>
public abstract class ModelProviderBase : IModelProvider
{
    protected readonly ILogger Logger;
    protected readonly HttpClient HttpClient;

    protected ModelProviderBase(HttpClient httpClient, ILogger logger)
    {
        HttpClient = httpClient;
        Logger = logger;
    }

    public abstract string ProviderId { get; }
    public abstract string Name { get; }
    public abstract string ProviderType { get; }
    public abstract ProviderCapabilities Capabilities { get; }

    public abstract Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    public abstract IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);

    public virtual async Task<ProviderHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            stopwatch.Stop();

            return ProviderHealthResult.Healthy((int)stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Health check failed for provider {ProviderId}", ProviderId);
            return ProviderHealthResult.Unhealthy(ex.Message);
        }
    }

    public abstract Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply default model if not specified in request.
    /// </summary>
    protected ChatRequest ApplyDefaults(ChatRequest request, string defaultModel)
    {
        if (string.IsNullOrEmpty(request.Model))
        {
            return request with { Model = defaultModel };
        }
        return request;
    }

    /// <summary>
    /// Map provider-specific finish reasons to the common enum.
    /// </summary>
    protected static FinishReason MapFinishReason(string? reason)
    {
        return reason?.ToLowerInvariant() switch
        {
            "stop" => FinishReason.Stop,
            "end_turn" => FinishReason.Stop,
            "length" => FinishReason.Length,
            "max_tokens" => FinishReason.Length,
            "tool_calls" => FinishReason.ToolCalls,
            "tool_use" => FinishReason.ToolCalls,
            "content_filter" => FinishReason.ContentFilter,
            _ => FinishReason.Other
        };
    }
}

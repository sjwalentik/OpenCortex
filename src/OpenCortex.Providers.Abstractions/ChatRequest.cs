namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// A request to generate a chat completion.
/// </summary>
public sealed record ChatRequest
{
    /// <summary>
    /// Model identifier to use for this request.
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// Messages in the conversation.
    /// </summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>
    /// Tools available for the model to call.
    /// </summary>
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Additional options for the request.
    /// </summary>
    public ChatRequestOptions? Options { get; init; }
}

/// <summary>
/// Additional options for chat completion requests.
/// </summary>
public sealed record ChatRequestOptions
{
    /// <summary>
    /// Sampling temperature (0.0 to 2.0). Higher values increase randomness.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Maximum number of tokens to generate.
    /// </summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Nucleus sampling probability (0.0 to 1.0).
    /// </summary>
    public double? TopP { get; init; }

    /// <summary>
    /// Sequences that will stop generation when encountered.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0). Positive values penalize new topics.
    /// </summary>
    public double? PresencePenalty { get; init; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0). Positive values penalize repetition.
    /// </summary>
    public double? FrequencyPenalty { get; init; }

    /// <summary>
    /// Tool choice behavior.
    /// </summary>
    public ToolChoice? ToolChoice { get; init; }

    /// <summary>
    /// User identifier for abuse tracking.
    /// </summary>
    public string? User { get; init; }
}

/// <summary>
/// Controls how the model uses tools.
/// </summary>
public enum ToolChoice
{
    /// <summary>
    /// Model decides whether to use tools.
    /// </summary>
    Auto,

    /// <summary>
    /// Model will not use any tools.
    /// </summary>
    None,

    /// <summary>
    /// Model must use at least one tool.
    /// </summary>
    Required
}

namespace OpenCortex.Tools;

/// <summary>
/// Context for tool execution, containing user identity and credentials.
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>
    /// User ID (derived from Firebase UID or native user ID).
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Customer/tenant ID.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Conversation ID for tracking.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Path to the user's sandboxed workspace directory.
    /// </summary>
    public string? WorkspacePath { get; init; }

    /// <summary>
    /// Decrypted credentials keyed by provider ID (e.g., "github" -> PAT).
    /// Loaded once per request for efficiency.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Credentials { get; init; }

    /// <summary>
    /// Command execution mode for this user: "auto" or "approval".
    /// </summary>
    public string CommandMode { get; init; } = "approval";

    /// <summary>
    /// Get a credential for a specific provider.
    /// </summary>
    public string? GetCredential(string providerId)
    {
        return Credentials?.GetValueOrDefault(providerId.ToLowerInvariant());
    }
}

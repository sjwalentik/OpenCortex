namespace OpenCortex.Tools;

/// <summary>
/// Context for tool execution, containing user identity and credentials.
/// </summary>
public sealed record ToolExecutionContext
{
    /// <summary>
    /// Stable internal GUID used for executor/workspace correlation.
    /// Hosted authorization should prefer TenantUserId because hosted and MCP flows may derive this GUID differently.
    /// </summary>
    public required Guid UserId { get; init; }

    /// <summary>
    /// Stable internal tenant GUID used for executor/workspace correlation.
    /// Hosted authorization should prefer TenantCustomerId because hosted and MCP flows may derive this GUID differently.
    /// </summary>
    public required Guid CustomerId { get; init; }

    /// <summary>
    /// Conversation ID for tracking.
    /// </summary>
    public required string ConversationId { get; init; }

    /// <summary>
    /// Hosted tenant user ID when available. Memory tools and hosted authorization should prefer this value.
    /// </summary>
    public string? TenantUserId { get; init; }

    /// <summary>
    /// Hosted tenant customer ID when available. Memory tools and hosted authorization should prefer this value.
    /// </summary>
    public string? TenantCustomerId { get; init; }

    /// <summary>
    /// Active brain ID from the routing context when available.
    /// </summary>
    public string? BrainId { get; init; }

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

namespace OpenCortex.Orchestration;

/// <summary>
/// Scoped holder for the internal tenant user/customer IDs (from the users/customers tables).
/// Set at the start of each request by the API layer so that UserProviderFactory can create
/// user-scoped tokens that satisfy the api_tokens.user_id FK constraint.
/// </summary>
public sealed class WorkspaceTenantIds
{
    /// <summary>Internal users.user_id string (e.g. "user_abc123").</summary>
    public string? UserId { get; set; }

    /// <summary>Internal customers.customer_id string (e.g. "cust_abc123").</summary>
    public string? CustomerId { get; set; }
}

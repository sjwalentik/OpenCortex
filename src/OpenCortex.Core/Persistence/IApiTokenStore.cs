namespace OpenCortex.Core.Persistence;

public interface IApiTokenStore
{
    Task<IReadOnlyList<ApiTokenSummary>> ListTokensAsync(
        string userId,
        string customerId,
        CancellationToken cancellationToken = default);

    Task<ApiTokenRecord> CreateTokenAsync(
        ApiTokenCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiTokenAuthenticationRecord?> GetActiveTokenByHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    Task TouchLastUsedAsync(
        string apiTokenId,
        CancellationToken cancellationToken = default);

    Task<bool> RevokeTokenAsync(
        string apiTokenId,
        string userId,
        string customerId,
        CancellationToken cancellationToken = default);
}

public sealed record ApiTokenCreateRequest(
    string UserId,
    string CustomerId,
    string Name,
    string TokenHash,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt);

public sealed record ApiTokenRecord(
    string ApiTokenId,
    string UserId,
    string CustomerId,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record ApiTokenSummary(
    string ApiTokenId,
    string Name,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevokedAt);

public sealed record ApiTokenAuthenticationRecord(
    string ApiTokenId,
    string UserId,
    string CustomerId,
    string TokenPrefix,
    IReadOnlyList<string> Scopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt);

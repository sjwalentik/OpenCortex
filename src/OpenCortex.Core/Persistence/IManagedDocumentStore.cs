namespace OpenCortex.Core.Persistence;

public interface IManagedDocumentStore
{
    Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
        string customerId,
        string brainId,
        int limit = 200,
        CancellationToken cancellationToken = default);

    Task<int> CountActiveManagedDocumentsAsync(
        string customerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(
        string customerId,
        string brainId,
        CancellationToken cancellationToken = default);

    Task<ManagedDocumentDetail?> GetManagedDocumentAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        CancellationToken cancellationToken = default);

    Task<ManagedDocumentDetail> CreateManagedDocumentAsync(
        ManagedDocumentCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(
        ManagedDocumentUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteManagedDocumentAsync(
        string customerId,
        string brainId,
        string managedDocumentId,
        string userId,
        CancellationToken cancellationToken = default);
}

public sealed record ManagedDocumentCreateRequest(
    string BrainId,
    string CustomerId,
    string Title,
    string? Slug,
    string Content,
    IReadOnlyDictionary<string, string> Frontmatter,
    string Status,
    string UserId);

public sealed record ManagedDocumentUpdateRequest(
    string ManagedDocumentId,
    string BrainId,
    string CustomerId,
    string Title,
    string? Slug,
    string Content,
    IReadOnlyDictionary<string, string> Frontmatter,
    string Status,
    string UserId);

public sealed record ManagedDocumentSummary(
    string ManagedDocumentId,
    string BrainId,
    string CustomerId,
    string Title,
    string Slug,
    string CanonicalPath,
    string Status,
    int WordCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ManagedDocumentDetail(
    string ManagedDocumentId,
    string BrainId,
    string CustomerId,
    string Title,
    string Slug,
    string CanonicalPath,
    string Content,
    IReadOnlyDictionary<string, string> Frontmatter,
    string ContentHash,
    string Status,
    int WordCount,
    string CreatedBy,
    string UpdatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsDeleted);

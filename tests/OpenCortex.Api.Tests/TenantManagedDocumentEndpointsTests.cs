using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Api;
using OpenCortex.Core.Authoring;
using OpenCortex.Core.Brains;
using OpenCortex.Core.Persistence;

namespace OpenCortex.Api.Tests;

public sealed class TenantManagedDocumentEndpointsTests
{
    [Fact]
    public async Task GetDocumentByCanonicalPath_ReturnsDocument_WhenFound()
    {
        var brainCatalogStore = new StubBrainCatalogStore("managed-content");
        var managedDocumentStore = new StubManagedDocumentStore();
        await managedDocumentStore.CreateManagedDocumentAsync(
            new ManagedDocumentCreateRequest(
                "brain_test123",
                "cust_test123",
                "Pixel",
                "identity/pixel",
                "# Pixel",
                new Dictionary<string, string>(),
                "published",
                "user_test"),
            CancellationToken.None);

        var result = await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            "cust_test123",
            "brain_test123",
            "identity/pixel.md",
            brainCatalogStore,
            managedDocumentStore,
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(json);
        Assert.Equal("identity/pixel.md", json!.RootElement.GetProperty("canonicalPath").GetString());
        Assert.Equal("# Pixel", json.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task GetDocumentByCanonicalPath_ReturnsBadRequest_WhenCanonicalPathMissing()
    {
        var result = await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            "cust_test123",
            "brain_test123",
            null,
            new StubBrainCatalogStore("managed-content"),
            new StubManagedDocumentStore(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.NotNull(json);
        Assert.Contains("canonicalPath", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocumentByCanonicalPath_ReturnsBadRequest_WhenBrainIsNotManagedContent()
    {
        var result = await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            "cust_test123",
            "brain_test123",
            "identity/pixel.md",
            new StubBrainCatalogStore("filesystem"),
            new StubManagedDocumentStore(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.NotNull(json);
        Assert.Contains("managed-content", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocumentByCanonicalPath_ReturnsNotFound_WhenBrainMissing()
    {
        var result = await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            "cust_test123",
            "brain_missing",
            "identity/pixel.md",
            new StubBrainCatalogStore("managed-content"),
            new StubManagedDocumentStore(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.NotNull(json);
        Assert.Contains("brain_missing", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetDocumentByCanonicalPath_ReturnsNotFound_WhenDocumentMissing()
    {
        var result = await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            "cust_test123",
            "brain_test123",
            "identity/pixel.md",
            new StubBrainCatalogStore("managed-content"),
            new StubManagedDocumentStore(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        Assert.NotNull(json);
        Assert.Contains("identity/pixel.md", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int StatusCode, JsonDocument? Json)> ExecuteResultAsync(IResult result)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.RequestServices = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .ConfigureHttpJsonOptions(_ => { })
            .BuildServiceProvider();
        httpContext.Response.Body = new MemoryStream();

        await result.ExecuteAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        if (httpContext.Response.Body.Length == 0)
        {
            return (httpContext.Response.StatusCode, null);
        }

        var json = await JsonDocument.ParseAsync(httpContext.Response.Body);
        return (httpContext.Response.StatusCode, json);
    }

    private sealed class StubBrainCatalogStore(string mode) : IBrainCatalogStore
    {
        private readonly BrainDetail _brain = new(
            "brain_test123",
            "Test Brain",
            "test-brain",
            mode,
            "active",
            null,
            "cust_test123",
            []);

        public Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BrainSummary>>([]);

        public Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<BrainSummary>>([]);

        public Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
            => Task.FromResult<BrainDetail?>(brainId == _brain.BrainId ? _brain : null);

        public Task<BrainDetail?> GetBrainByCustomerAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => Task.FromResult<BrainDetail?>(
                customerId == _brain.CustomerId && brainId == _brain.BrainId ? _brain : null);

        public Task<BrainDetail> CreateBrainAsync(BrainDefinition brain, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<BrainDetail?> UpdateBrainAsync(string brainId, string name, string slug, string mode, string status, string? description, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary> AddSourceRootAsync(string brainId, SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary?> UpdateSourceRootAsync(string brainId, string sourceRootId, string path, string pathType, bool isWritable, string[] includePatterns, string[] excludePatterns, string watchMode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubManagedDocumentStore : IManagedDocumentStore
    {
        private readonly Dictionary<string, ManagedDocumentDetail> _documents = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId = 1;

        public Task<IReadOnlyList<ManagedDocumentSummary>> ListManagedDocumentsAsync(
            string customerId,
            string brainId,
            string? pathPrefix = null,
            string? excludePathPrefix = null,
            int limit = 200,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ManagedDocumentSummary>>(_documents.Values
                .Where(document => string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                    && !document.IsDeleted
                    && MatchesPathPrefix(document.CanonicalPath, pathPrefix)
                    && !HasExcludedPathPrefix(document.CanonicalPath, excludePathPrefix))
                .Take(limit)
                .Select(document => new ManagedDocumentSummary(
                    document.ManagedDocumentId,
                    document.BrainId,
                    document.CustomerId,
                    document.Title,
                    document.Slug,
                    document.CanonicalPath,
                    document.Status,
                    document.WordCount,
                    document.CreatedAt,
                    document.UpdatedAt))
                .ToList());

        public Task<int> CountActiveManagedDocumentsAsync(string customerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<ManagedDocumentDetail>> ListManagedDocumentsForIndexingAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentDetail?> GetManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentDetail?> GetManagedDocumentByCanonicalPathAsync(string customerId, string brainId, string canonicalPath, CancellationToken cancellationToken = default)
            => Task.FromResult<ManagedDocumentDetail?>(_documents.Values.FirstOrDefault(document =>
                string.Equals(document.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.BrainId, brainId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(document.CanonicalPath, canonicalPath, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted));

        public Task<IReadOnlyList<ManagedDocumentVersionSummary>> ListManagedDocumentVersionsAsync(string customerId, string brainId, string managedDocumentId, int limit = 50, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentVersionDetail?> GetManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentDetail> CreateManagedDocumentAsync(ManagedDocumentCreateRequest request, CancellationToken cancellationToken = default)
        {
            var activeDocuments = _documents.Values.Count(document =>
                string.Equals(document.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase)
                && !document.IsDeleted);
            if (request.MaxActiveDocuments is int maxActiveDocuments && activeDocuments >= maxActiveDocuments)
            {
                throw new ManagedDocumentQuotaExceededException(
                    request.QuotaExceededMessage
                    ?? $"Document limit reached for customer '{request.CustomerId}'.");
            }

            var now = DateTimeOffset.UtcNow;
            var managedDocumentId = $"md-{_nextId++:D4}";
            var slug = request.Slug ?? request.Title;
            var content = request.Content ?? string.Empty;
            var document = new ManagedDocumentDetail(
                managedDocumentId,
                request.BrainId,
                request.CustomerId,
                request.Title,
                slug,
                ManagedDocumentText.BuildCanonicalPath(slug),
                content,
                new Dictionary<string, string>(request.Frontmatter, StringComparer.OrdinalIgnoreCase),
                $"hash-{managedDocumentId}",
                request.Status,
                1,
                request.UserId,
                request.UserId,
                now,
                now,
                false);

            _documents[managedDocumentId] = document;
            return Task.FromResult(document);
        }

        public Task<ManagedDocumentDetail?> UpdateManagedDocumentAsync(ManagedDocumentUpdateRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> SoftDeleteManagedDocumentAsync(string customerId, string brainId, string managedDocumentId, string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ManagedDocumentDetail?> RestoreManagedDocumentVersionAsync(string customerId, string brainId, string managedDocumentId, string managedDocumentVersionId, string userId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        private static bool MatchesPathPrefix(string canonicalPath, string? pathPrefix)
        {
            if (string.IsNullOrWhiteSpace(pathPrefix))
            {
                return true;
            }

            var normalizedPrefix = pathPrefix.Trim().Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return true;
            }

            return canonicalPath.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasExcludedPathPrefix(string canonicalPath, string? pathPrefix)
            => !string.IsNullOrWhiteSpace(pathPrefix) && MatchesPathPrefix(canonicalPath, pathPrefix);
    }
}

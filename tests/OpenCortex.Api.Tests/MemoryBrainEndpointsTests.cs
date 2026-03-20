using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenCortex.Api;
using OpenCortex.Core.Brains;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using OpenCortex.Orchestration.Memory;

namespace OpenCortex.Api.Tests;

public sealed class MemoryBrainEndpointsTests
{
    [Fact]
    public async Task GetMemoryBrainAsync_ReturnsNeedsConfiguration_WhenMultipleBrainsExistWithoutPreference()
    {
        var tenantStore = new StubTenantCatalogStore();
        var brainStore = new StubBrainCatalogStore(
            new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
            new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0));
        var preferenceStore = new StubUserMemoryPreferenceStore();
        var resolver = new MemoryBrainResolver(brainStore, preferenceStore);

        var result = await MemoryBrainEndpoints.GetMemoryBrainAsync(
            CreateUser(),
            tenantStore,
            brainStore,
            preferenceStore,
            resolver,
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(json);
        Assert.True(json!.RootElement.GetProperty("needsConfiguration").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("availableBrains").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("configuredMemoryBrainId").ValueKind);
    }

    [Fact]
    public async Task UpdateMemoryBrainAsync_SavesConfiguredBrainAndReturnsEffectiveSelection()
    {
        var tenantStore = new StubTenantCatalogStore();
        var brainStore = new StubBrainCatalogStore(
            new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
            new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0));
        var preferenceStore = new StubUserMemoryPreferenceStore();
        var resolver = new MemoryBrainResolver(brainStore, preferenceStore);

        var result = await MemoryBrainEndpoints.UpdateMemoryBrainAsync(
            new UpdateMemoryBrainRequest("brain-b"),
            CreateUser(),
            tenantStore,
            brainStore,
            preferenceStore,
            resolver,
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(json);
        Assert.Equal("brain-b", preferenceStore.MemoryBrainId);
        Assert.Equal("brain-b", json!.RootElement.GetProperty("configuredMemoryBrainId").GetString());
        Assert.Equal("brain-b", json.RootElement.GetProperty("effectiveMemoryBrainId").GetString());
        Assert.False(json.RootElement.GetProperty("needsConfiguration").GetBoolean());
    }

    [Fact]
    public async Task UpdateMemoryBrainAsync_ReturnsBadRequest_WhenBrainIsNotActiveManagedContent()
    {
        var tenantStore = new StubTenantCatalogStore();
        var brainStore = new StubBrainCatalogStore(
            new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
            new BrainSummary("brain-fs", "Filesystem", "brain-fs", "filesystem", "active", 0));
        var preferenceStore = new StubUserMemoryPreferenceStore();
        var resolver = new MemoryBrainResolver(brainStore, preferenceStore);

        var result = await MemoryBrainEndpoints.UpdateMemoryBrainAsync(
            new UpdateMemoryBrainRequest("brain-fs"),
            CreateUser(),
            tenantStore,
            brainStore,
            preferenceStore,
            resolver,
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.NotNull(json);
        Assert.Contains("managed-content", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static ClaimsPrincipal CreateUser()
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-test"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim("name", "Test User")
        ], "Test"));

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

    private sealed class StubTenantCatalogStore : ITenantCatalogStore
    {
        public Task<TenantContext> EnsureTenantContextAsync(AuthenticatedUserProfile profile, CancellationToken cancellationToken = default)
            => Task.FromResult(new TenantContext(
                UserId: profile.ExternalId,
                ExternalId: profile.ExternalId,
                Email: profile.Email,
                DisplayName: profile.DisplayName,
                AvatarUrl: profile.AvatarUrl,
                CustomerId: "cust-test",
                CustomerSlug: "cust-test",
                CustomerName: "Test Workspace",
                Role: "owner",
                PlanId: "free",
                SubscriptionStatus: "active",
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: false,
                BrainId: "brain-a",
                BrainSlug: "brain-a",
                BrainName: "Brain A"));
    }

    private sealed class StubUserMemoryPreferenceStore : IUserMemoryPreferenceStore
    {
        public string? MemoryBrainId { get; private set; }

        public Task<string?> GetMemoryBrainIdAsync(string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(MemoryBrainId);

        public Task SetMemoryBrainIdAsync(string userId, string? memoryBrainId, CancellationToken cancellationToken = default)
        {
            MemoryBrainId = memoryBrainId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubBrainCatalogStore(params BrainSummary[] brains) : IBrainCatalogStore
    {
        private readonly IReadOnlyList<BrainSummary> _brains = brains;

        public Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_brains);

        public Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(string customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(_brains);

        public Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<BrainDetail?> GetBrainByCustomerAsync(string customerId, string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

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
}

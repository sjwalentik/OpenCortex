using OpenCortex.Core.Persistence;
using OpenCortex.Orchestration.Memory;

namespace OpenCortex.Api.Tests;

public sealed class MemoryBrainResolverTests
{
    [Fact]
    public async Task ResolveAsync_ReturnsOnlyActiveManagedContentBrain_WhenExactlyOneExists()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-docs", "Docs", "docs", "managed-content", "active", 0)),
            new StubUserMemoryPreferenceStore());

        var result = await resolver.ResolveAsync("cust-a", "user-a");

        Assert.True(result.Success);
        Assert.Equal("brain-docs", result.BrainId);
        Assert.False(result.NeedsConfiguration);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNeedsConfiguration_WhenMultipleActiveManagedContentBrainsExistWithoutPreference()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
                new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0)),
            new StubUserMemoryPreferenceStore());

        var result = await resolver.ResolveAsync("cust-a", "user-a");

        Assert.False(result.Success);
        Assert.True(result.NeedsConfiguration);
        Assert.Null(result.BrainId);
        Assert.Contains("Configure a memory brain", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNeedsConfiguration_WhenConfiguredBrainIsMissingOrInactive()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
                new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0)),
            new StubUserMemoryPreferenceStore("brain-missing"));

        var result = await resolver.ResolveAsync("cust-a", "user-a");

        Assert.False(result.Success);
        Assert.True(result.NeedsConfiguration);
        Assert.Null(result.BrainId);
        Assert.Contains("not found", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsConfiguredBrain_WhenPreferenceMatchesActiveManagedContentBrain()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
                new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0),
                new BrainSummary("brain-fs", "Filesystem", "filesystem", "filesystem", "active", 0)),
            new StubUserMemoryPreferenceStore("brain-b"));

        var result = await resolver.ResolveAsync("cust-a", "user-a");

        Assert.True(result.Success);
        Assert.Equal("brain-b", result.BrainId);
        Assert.False(result.NeedsConfiguration);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task ResolveAsync_UsesCustomerScopedPreference_WhenSameUserHasDifferentWorkspaceSelections()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-a", "Brain A", "brain-a", "managed-content", "active", 0),
                new BrainSummary("brain-b", "Brain B", "brain-b", "managed-content", "active", 0)),
            new StubUserMemoryPreferenceStore(new Dictionary<(string CustomerId, string UserId), string?>
            {
                [("cust-a", "user-a")] = "brain-a",
                [("cust-b", "user-a")] = "brain-b"
            }));

        var resultA = await resolver.ResolveAsync("cust-a", "user-a");
        var resultB = await resolver.ResolveAsync("cust-b", "user-a");

        Assert.True(resultA.Success);
        Assert.Equal("brain-a", resultA.BrainId);
        Assert.True(resultB.Success);
        Assert.Equal("brain-b", resultB.BrainId);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsError_WhenNoActiveManagedContentBrainsExist()
    {
        var resolver = new MemoryBrainResolver(
            new StubBrainCatalogStore(
                new BrainSummary("brain-fs", "Filesystem", "filesystem", "filesystem", "active", 0),
                new BrainSummary("brain-archived", "Archive", "archive", "managed-content", "retired", 0)),
            new StubUserMemoryPreferenceStore());

        var result = await resolver.ResolveAsync("cust-a", "user-a");

        Assert.False(result.Success);
        Assert.False(result.NeedsConfiguration);
        Assert.Null(result.BrainId);
        Assert.Contains("No active managed-content brains", result.Error, StringComparison.OrdinalIgnoreCase);
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

        public Task<BrainDetail> CreateBrainAsync(OpenCortex.Core.Brains.BrainDefinition brain, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<BrainDetail?> UpdateBrainAsync(string brainId, string name, string slug, string mode, string status, string? description, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UpsertBrainsAsync(IReadOnlyList<OpenCortex.Core.Brains.BrainDefinition> brains, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary> AddSourceRootAsync(string brainId, OpenCortex.Core.Brains.SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary?> UpdateSourceRootAsync(string brainId, string sourceRootId, string path, string pathType, bool isWritable, string[] includePatterns, string[] excludePatterns, string watchMode, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class StubUserMemoryPreferenceStore : IUserMemoryPreferenceStore
    {
        private readonly IReadOnlyDictionary<(string CustomerId, string UserId), string?> _memoryBrainIds;

        public StubUserMemoryPreferenceStore(string? memoryBrainId = null)
            : this(new Dictionary<(string CustomerId, string UserId), string?>
            {
                [("cust-a", "user-a")] = memoryBrainId
            })
        {
        }

        public StubUserMemoryPreferenceStore(IReadOnlyDictionary<(string CustomerId, string UserId), string?> memoryBrainIds)
        {
            _memoryBrainIds = memoryBrainIds;
        }

        public Task<string?> GetMemoryBrainIdAsync(string customerId, string userId, CancellationToken cancellationToken = default)
            => Task.FromResult(_memoryBrainIds.TryGetValue((customerId, userId), out var memoryBrainId) ? memoryBrainId : null);

        public Task SetMemoryBrainIdAsync(string customerId, string userId, string? newMemoryBrainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}

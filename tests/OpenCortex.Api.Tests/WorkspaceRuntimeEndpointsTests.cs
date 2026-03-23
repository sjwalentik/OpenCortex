using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCortex.Api;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using OpenCortex.Tools;

namespace OpenCortex.Api.Tests;

public sealed class WorkspaceRuntimeEndpointsTests
{
    [Fact]
    public async Task GetWorkspaceRuntimeAsync_ReturnsDefaultProfile_WhenNoPreferenceIsSaved()
    {
        var result = await WorkspaceRuntimeEndpoints.GetWorkspaceRuntimeAsync(
            CreateUser(),
            new StubTenantCatalogStore(),
            new StubUserWorkspaceRuntimeProfileStore(),
            Options.Create(new ToolsOptions()),
            new StubWorkspaceManager(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.NotNull(json);
        Assert.Equal("default", json!.RootElement.GetProperty("effectiveProfileId").GetString());
        Assert.True(json.RootElement.GetProperty("supportsManagedProfiles").GetBoolean());
    }

    [Fact]
    public async Task UpdateWorkspaceRuntimeAsync_SavesProfile_AndRequestsRestartWhenChanged()
    {
        var store = new StubUserWorkspaceRuntimeProfileStore();
        var workspaceManager = new StubWorkspaceManager();
        var options = Options.Create(new ToolsOptions
        {
            DotNet10ContainerImage = "ghcr.io/test/agent-runtime-dotnet10:develop"
        });

        var result = await WorkspaceRuntimeEndpoints.UpdateWorkspaceRuntimeAsync(
            new UpdateWorkspaceRuntimeRequest("dotnet10"),
            CreateUser(),
            new StubTenantCatalogStore(),
            store,
            options,
            workspaceManager,
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status200OK, statusCode);
        Assert.Equal("dotnet10", store.ProfileId);
        Assert.True(workspaceManager.StopCalled);
        Assert.True(json!.RootElement.GetProperty("restartRequested").GetBoolean());
    }

    [Fact]
    public async Task UpdateWorkspaceRuntimeAsync_ReturnsBadRequest_WhenProfileIsUnavailable()
    {
        var result = await WorkspaceRuntimeEndpoints.UpdateWorkspaceRuntimeAsync(
            new UpdateWorkspaceRuntimeRequest("dotnet10"),
            CreateUser(),
            new StubTenantCatalogStore(),
            new StubUserWorkspaceRuntimeProfileStore(),
            Options.Create(new ToolsOptions()),
            new StubWorkspaceManager(),
            CancellationToken.None);

        var (statusCode, json) = await ExecuteResultAsync(result);

        Assert.Equal(StatusCodes.Status400BadRequest, statusCode);
        Assert.NotNull(json);
        Assert.Contains("not available", json!.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
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
                UserId: "user_row",
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

    private sealed class StubUserWorkspaceRuntimeProfileStore : IUserWorkspaceRuntimeProfileStore
    {
        public string? ProfileId { get; private set; }

        public Task<string?> GetProfileIdAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(ProfileId);

        public Task SetProfileIdAsync(Guid userId, string? profileId, CancellationToken cancellationToken = default)
        {
            ProfileId = profileId;
            return Task.CompletedTask;
        }
    }

    private sealed class StubWorkspaceManager : IWorkspaceManager
    {
        public bool StopCalled { get; private set; }

        public bool SupportsContainerIsolation => true;

        public Task CleanupExpiredWorkspacesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteWorkspaceAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkspaceStatus> EnsureRunningAsync(Guid userId, IReadOnlyDictionary<string, string>? credentials = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceStatus { UserId = userId, State = WorkspaceState.Running, WorkspacePath = "/workspace" });
        public Task<CommandResult> ExecuteCommandAsync(Guid userId, string command, string? arguments = null, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environmentVariables = null, IReadOnlyList<string>? argumentList = null, string? standardInput = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new CommandResult { ExitCode = 0, StandardOutput = string.Empty, StandardError = string.Empty, Duration = TimeSpan.Zero });
        public Task<WorkspaceStatus> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WorkspaceStatus { UserId = userId, State = WorkspaceState.Running, WorkspacePath = "/workspace" });
        public Task<string> GetWorkspacePathAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult("/workspace");
        public bool IsPathAllowed(Guid userId, string path) => true;
        public string ResolvePath(Guid userId, string relativePath) => $"/workspace/{relativePath.TrimStart('/')}";
        public Task StopAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            return Task.CompletedTask;
        }
    }
}

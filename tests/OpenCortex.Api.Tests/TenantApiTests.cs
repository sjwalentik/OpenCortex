using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Core.Brains;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;

namespace OpenCortex.Api.Tests;

/// <summary>
/// Tests for tenant API authentication and authorization.
/// These tests validate that:
/// - Unauthenticated requests are rejected
/// - Invalid claims are handled correctly
/// - Valid tokens are accepted
///
/// Note: Full integration tests require a running database.
/// These tests focus on the auth flow using mocked services.
/// </summary>
public sealed class TenantApiTests : IClassFixture<TenantApiTests.TenantApiWebApplicationFactory>
{
    private readonly TenantApiWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TenantApiTests(TenantApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // -------------------------------------------------------------------------
    // GET /tenant/me - Authorization tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMe_WithoutAuth_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/tenant/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithMissingClaims_ReturnsProblem()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "missing-claims");

        var response = await _client.GetAsync("/tenant/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithMissingEmailClaim_ReturnsProblem()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "missing-email");

        var response = await _client.GetAsync("/tenant/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ReturnsTenantContext()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "valid-user");

        var response = await _client.GetAsync("/tenant/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.Equal("user_test123", json.RootElement.GetProperty("userId").GetString());
        Assert.Equal("firebase-uid-123", json.RootElement.GetProperty("externalId").GetString());
        Assert.Equal("test@example.com", json.RootElement.GetProperty("email").GetString());
        Assert.Equal("Test User", json.RootElement.GetProperty("displayName").GetString());
    }

    // -------------------------------------------------------------------------
    // GET /tenant/brains - Authorization tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBrains_WithoutAuth_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/tenant/brains");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // NOTE: GetBrains_WithValidToken and GetBrain tests require a database connection
    // because the API captures brainCatalogStore directly at startup rather than via DI.
    // Full integration tests should be run against a real database.

    // -------------------------------------------------------------------------
    // GET /tenant/brains/{brainId} - Authorization tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBrain_WithoutAuth_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/tenant/brains/brain_test123");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetDocumentByPath_WithoutAuth_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var response = await _client.GetAsync("/tenant/brains/brain_test123/documents/by-path?canonicalPath=identity/pixel.md");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST /tenant/query - Authorization tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Query_WithoutAuth_ReturnsUnauthorized()
    {
        _client.DefaultRequestHeaders.Authorization = null;

        var request = new { oql = """FROM brain("brain_test123") SEARCH "test" LIMIT 5""" };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/tenant/query", content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Query_InvalidOql_ReturnsBadRequest()
    {
        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Test", "valid-user");

        var request = new { oql = "INVALID QUERY SYNTAX" };
        var content = new StringContent(
            JsonSerializer.Serialize(request),
            Encoding.UTF8,
            "application/json");

        var response = await _client.PostAsync("/tenant/query", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RateLimitedEndpoint_ReturnsPolicyHeadersAndProblemDetails()
    {
        var firstResponse = await _client.GetAsync("/_testing/rate-limit");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var secondResponse = await _client.GetAsync("/_testing/rate-limit");

        Assert.Equal(HttpStatusCode.TooManyRequests, secondResponse.StatusCode);
        Assert.Equal("testing-low-limit", secondResponse.Headers.GetValues("X-RateLimit-Policy").Single());
        Assert.Equal("1", secondResponse.Headers.GetValues("X-RateLimit-Limit").Single());
        Assert.Equal("0", secondResponse.Headers.GetValues("X-RateLimit-Remaining").Single());
        Assert.Equal("60", secondResponse.Headers.GetValues("X-RateLimit-Window-Seconds").Single());

        Assert.True(secondResponse.Headers.TryGetValues("X-RateLimit-Retry-After-Seconds", out var retryAfterValues));
        Assert.True(int.TryParse(retryAfterValues.Single(), out var retryAfterSeconds));
        Assert.True(retryAfterSeconds > 0);

        var content = await secondResponse.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(content);

        Assert.Equal("Request rate limit exceeded.", json.RootElement.GetProperty("title").GetString());
        Assert.Equal(429, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("testing-low-limit", json.RootElement.GetProperty("policy").GetString());
        Assert.Equal("/_testing/rate-limit", json.RootElement.GetProperty("route").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("limit").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("remaining").GetInt32());
        Assert.Equal(60, json.RootElement.GetProperty("windowSeconds").GetInt32());
        Assert.True(json.RootElement.GetProperty("retryAfterSeconds").GetInt32() > 0);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    // NOTE: Query_BrainNotInWorkspace requires a database connection
    // because the API captures brainCatalogStore directly at startup rather than via DI.

    // -------------------------------------------------------------------------
    // Test Infrastructure
    // -------------------------------------------------------------------------

    public sealed class TenantApiWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            // Set environment variables for configuration
            Environment.SetEnvironmentVariable("OpenCortex__HostedAuth__Enabled", "true");
            Environment.SetEnvironmentVariable("OpenCortex__HostedAuth__FirebaseProjectId", "test-project");
            Environment.SetEnvironmentVariable("OpenCortex__Database__ConnectionString", "Host=localhost;Database=opencortex_test");

            builder.ConfigureTestServices(services =>
            {
                // Replace the default JWT authentication with test authentication
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", options => { });

                // Replace real stores with stubs
                var tenantStoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ITenantCatalogStore));
                if (tenantStoreDescriptor != null)
                {
                    services.Remove(tenantStoreDescriptor);
                }
                services.AddSingleton<ITenantCatalogStore>(new StubTenantCatalogStore());

                var brainStoreDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IBrainCatalogStore));
                if (brainStoreDescriptor != null)
                {
                    services.Remove(brainStoreDescriptor);
                }
                services.AddSingleton<IBrainCatalogStore>(new StubBrainCatalogStore());
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var headerValue = authHeader.ToString();
            if (!headerValue.StartsWith("Test ", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var token = headerValue["Test ".Length..];

            if (token == "missing-claims")
            {
                // Create a principal with missing required claims (no subject)
                var claims = new[] { new Claim("random", "value") };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            if (token == "missing-email")
            {
                // Create a principal with subject but no email
                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "firebase-uid-123") };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            if (token == "valid-user")
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "firebase-uid-123"),
                    new Claim(ClaimTypes.Email, "test@example.com"),
                    new Claim("name", "Test User"),
                    new Claim("picture", "https://example.com/avatar.jpg"),
                };
                var identity = new ClaimsIdentity(claims, "Test");
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, "Test");
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }

            return Task.FromResult(AuthenticateResult.Fail("Invalid test token"));
        }
    }

    private sealed class StubTenantCatalogStore : ITenantCatalogStore
    {
        public Task<TenantContext> EnsureTenantContextAsync(
            AuthenticatedUserProfile profile,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TenantContext(
                UserId: "user_test123",
                ExternalId: profile.ExternalId,
                Email: profile.Email,
                DisplayName: profile.DisplayName,
                AvatarUrl: profile.AvatarUrl,
                CustomerId: "cust_test123",
                CustomerSlug: "personal-test-12345678",
                CustomerName: "Test User's Workspace",
                Role: "owner",
                PlanId: "free",
                SubscriptionStatus: "active",
                CurrentPeriodEnd: null,
                CancelAtPeriodEnd: false,
                BrainId: "brain_test123",
                BrainSlug: "personal-test-12345678",
                BrainName: "Test User's Brain"));
        }
    }

    private sealed class StubBrainCatalogStore : IBrainCatalogStore
    {
        private readonly BrainDetail _testBrain = new(
            BrainId: "brain_test123",
            Name: "Test User's Brain",
            Slug: "personal-test-12345678",
            Mode: "managed-content",
            Status: "active",
            Description: "Test brain",
            CustomerId: "cust_test123",
            SourceRoots: []);

        public Task<IReadOnlyList<BrainSummary>> ListBrainsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<BrainSummary>>(
            [
                new BrainSummary(
                    _testBrain.BrainId,
                    _testBrain.Name,
                    _testBrain.Slug,
                    _testBrain.Mode,
                    _testBrain.Status,
                    0)
            ]);
        }

        public Task<IReadOnlyList<BrainSummary>> ListBrainsByCustomerAsync(
            string customerId,
            CancellationToken cancellationToken = default)
        {
            if (customerId == "cust_test123")
            {
                return ListBrainsAsync(cancellationToken);
            }

            return Task.FromResult<IReadOnlyList<BrainSummary>>([]);
        }

        public Task<BrainDetail?> GetBrainAsync(string brainId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(brainId == _testBrain.BrainId ? _testBrain : null);
        }

        public Task<BrainDetail?> GetBrainByCustomerAsync(
            string customerId,
            string brainId,
            CancellationToken cancellationToken = default)
        {
            if (customerId == "cust_test123" && brainId == _testBrain.BrainId)
            {
                return Task.FromResult<BrainDetail?>(_testBrain);
            }

            return Task.FromResult<BrainDetail?>(null);
        }

        public Task<BrainDetail> CreateBrainAsync(BrainDefinition brain, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<BrainDetail?> UpdateBrainAsync(
            string brainId,
            string name,
            string slug,
            string mode,
            string status,
            string? description,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RetireBrainAsync(string brainId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task UpsertBrainsAsync(IReadOnlyList<BrainDefinition> brains, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary> AddSourceRootAsync(string brainId, SourceRootDefinition sourceRoot, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<SourceRootSummary?> UpdateSourceRootAsync(
            string brainId,
            string sourceRootId,
            string path,
            string pathType,
            bool isWritable,
            string[] includePatterns,
            string[] excludePatterns,
            string watchMode,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<bool> RemoveSourceRootAsync(string brainId, string sourceRootId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}

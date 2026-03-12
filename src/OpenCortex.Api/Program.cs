using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenCortex.Api;
using OpenCortex.Core.Configuration;
using OpenCortex.Core.Embeddings;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Core.Security;
using OpenCortex.Core.Tenancy;
using OpenCortex.Indexer.Indexing;
using OpenCortex.Persistence.Postgres;
using OpenCortex.Retrieval.Execution;
using Stripe;
using BillingPortalSessionService = Stripe.BillingPortal.SessionService;
using BillingPortalSessionCreateOptions = Stripe.BillingPortal.SessionCreateOptions;
using CheckoutSession = Stripe.Checkout.Session;
using CheckoutSessionCreateOptions = Stripe.Checkout.SessionCreateOptions;
using CheckoutSessionLineItemOptions = Stripe.Checkout.SessionLineItemOptions;
using CheckoutSessionService = Stripe.Checkout.SessionService;

var builder = WebApplication.CreateBuilder(args);

var options = builder.Configuration.GetSection(OpenCortexOptions.SectionName).Get<OpenCortexOptions>() ?? new OpenCortexOptions();
var validationErrors = new OpenCortexOptionsValidator().Validate(options).ToList();
var connectionFactory = new PostgresConnectionFactory(new PostgresConnectionSettings
{
    ConnectionString = options.Database.ConnectionString,
});
if (!builder.Environment.IsEnvironment("Testing"))
{
    try
    {
        validationErrors.AddRange(await new PostgresEmbeddingSchemaValidator(connectionFactory)
            .ValidateAsync(options.Embeddings.Dimensions));
    }
    catch (Exception ex) when (ex is Npgsql.NpgsqlException or TimeoutException or InvalidOperationException)
    {
        validationErrors.Add($"Postgres schema validation failed: {ex.Message}");
    }
}

if (validationErrors.Count > 0)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, validationErrors));
}

var brainCatalogStore = new PostgresBrainCatalogStore(connectionFactory);
var tenantCatalogStore = new PostgresTenantCatalogStore(connectionFactory);
var managedDocumentStore = new PostgresManagedDocumentStore(connectionFactory);
var subscriptionStore = new PostgresSubscriptionStore(connectionFactory);
var apiTokenStore = new PostgresApiTokenStore(connectionFactory);
var usageCounterStore = new PostgresUsageCounterStore(connectionFactory);
var embeddingProvider = EmbeddingProviderFactory.Create(options.Embeddings);
var indexRunStore = new PostgresIndexRunStore(connectionFactory);
var hostedAuthConfigured = options.HostedAuth.Enabled
    && !string.IsNullOrWhiteSpace(options.HostedAuth.FirebaseProjectId);
var stripeConfigured = options.Billing.Stripe.Enabled
    && !string.IsNullOrWhiteSpace(options.Billing.Stripe.SecretKey)
    && !string.IsNullOrWhiteSpace(options.Billing.Stripe.WebhookSecret);

if (stripeConfigured)
{
    StripeConfiguration.ApiKey = options.Billing.Stripe.SecretKey;
}

ManagedContentBrainIndexingService BuildManagedContentIndexingService() =>
    new(
        managedDocumentStore,
        new PostgresDocumentCatalogStore(connectionFactory),
        new PostgresChunkStore(connectionFactory),
        new PostgresLinkGraphStore(connectionFactory),
        indexRunStore,
        new PostgresEmbeddingStore(connectionFactory),
        embeddingProvider);

PlanEntitlements ResolvePlanEntitlements(string? planId)
{
    if (!string.IsNullOrWhiteSpace(planId)
        && options.Billing.Plans.TryGetValue(planId, out var configuredPlan))
    {
        return configuredPlan;
    }

    return options.Billing.Plans["free"];
}

IResult BuildQuotaExceededResult(string planId, int currentUsage, PlanEntitlements plan) =>
    Results.Json(
        new
        {
            type = "quota_exceeded",
            title = "Document limit reached",
            detail = $"Your {planId} plan allows {plan.MaxDocuments} documents. Upgrade to continue adding more.",
            currentUsage,
            limit = plan.MaxDocuments,
            planId,
            upgradeUrl = "/tenant/billing/upgrade",
        },
        statusCode: StatusCodes.Status402PaymentRequired);

IResult BuildMcpQuotaExceededResult(string planId, long currentUsage, PlanEntitlements plan, DateTimeOffset resetAt) =>
    Results.Json(
        new
        {
            type = "quota_exceeded",
            title = "Monthly query limit reached",
            detail = $"Your {planId} plan allows {plan.McpQueriesPerMonth} hosted queries per month. Upgrade to continue.",
            currentUsage,
            limit = plan.McpQueriesPerMonth,
            planId,
            resetAt,
            upgradeUrl = "/tenant/billing/upgrade",
        },
        statusCode: StatusCodes.Status402PaymentRequired);

string BuildAppUrl(string relativePath) =>
    $"{options.Billing.Stripe.AppBaseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

const string StripeCheckoutSessionCompleted = "checkout.session.completed";
const string StripeCustomerSubscriptionCreated = "customer.subscription.created";
const string StripeCustomerSubscriptionUpdated = "customer.subscription.updated";
const string StripeCustomerSubscriptionDeleted = "customer.subscription.deleted";
const string StripeInvoicePaymentFailed = "invoice.payment_failed";
const string StripeInvoicePaymentSucceeded = "invoice.payment_succeeded";

string? ResolvePlanIdFromStripePriceId(string? stripePriceId)
{
    if (string.IsNullOrWhiteSpace(stripePriceId))
    {
        return null;
    }

    foreach (var pair in options.Billing.Stripe.PriceIds ?? [])
    {
        if (string.Equals(pair.Value, stripePriceId, StringComparison.Ordinal))
        {
            return pair.Key;
        }
    }

    return null;
}

async Task<string?> ResolveCustomerIdAsync(string? stripeCustomerId, IDictionary<string, string>? metadata, CancellationToken cancellationToken)
{
    if (metadata is not null
        && metadata.TryGetValue("customerId", out var metadataCustomerId)
        && !string.IsNullOrWhiteSpace(metadataCustomerId))
    {
        return metadataCustomerId;
    }

    if (!string.IsNullOrWhiteSpace(stripeCustomerId))
    {
        return await subscriptionStore.FindCustomerIdByStripeCustomerIdAsync(stripeCustomerId, cancellationToken);
    }

    return null;
}

DateTimeOffset? ToUtcDateTimeOffset(DateTime? value)
{
    if (!value.HasValue)
    {
        return null;
    }

    var timestamp = value.Value;
    if (timestamp.Kind == DateTimeKind.Unspecified)
    {
        timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
    }

    return timestamp.Kind == DateTimeKind.Utc
        ? new DateTimeOffset(timestamp)
        : new DateTimeOffset(timestamp.ToUniversalTime(), TimeSpan.Zero);
}

(DateTimeOffset? CurrentPeriodStart, DateTimeOffset? CurrentPeriodEnd) ResolveSubscriptionPeriod(Stripe.Subscription subscription)
{
    var periodStarts = subscription.Items.Data
        .Select(item => ToUtcDateTimeOffset(item.CurrentPeriodStart))
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .ToList();
    var periodEnds = subscription.Items.Data
        .Select(item => ToUtcDateTimeOffset(item.CurrentPeriodEnd))
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .ToList();

    return (
        periodStarts.Count == 0 ? null : periodStarts.Min(),
        periodEnds.Count == 0 ? null : periodEnds.Max());
}

EffectiveBillingState ResolveEffectiveBillingState(SubscriptionRecord subscription) =>
    HostedBillingStateResolver.Resolve(subscription, DateTimeOffset.UtcNow);

async Task<EffectiveBillingState> GetEffectiveBillingStateAsync(string customerId, CancellationToken cancellationToken)
{
    var subscription = await subscriptionStore.GetSubscriptionAsync(customerId, cancellationToken)
        ?? await subscriptionStore.EnsureFreeSubscriptionAsync(customerId, cancellationToken);

    return ResolveEffectiveBillingState(subscription);
}

const string DocumentsActiveCounterKey = "documents.active";

string BuildMonthlyQueryCounterKey(DateTimeOffset nowUtc) => $"mcp.queries.{nowUtc:yyyy-MM}";

DateTimeOffset BuildMonthlyQueryCounterResetAt(DateTimeOffset nowUtc) =>
    new DateTimeOffset(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);

async Task<UsageCounterRecord> SyncActiveDocumentCounterAsync(string customerId, CancellationToken cancellationToken)
{
    var activeDocuments = await managedDocumentStore.CountActiveManagedDocumentsAsync(customerId, cancellationToken);
    return await usageCounterStore.SetCounterAsync(
        new UsageCounterSetRequest(
            customerId,
            DocumentsActiveCounterKey,
            activeDocuments,
            null),
        cancellationToken);
}

async Task<UsageCounterRecord> GetMonthlyQueryUsageAsync(string customerId, CancellationToken cancellationToken)
{
    var nowUtc = DateTimeOffset.UtcNow;
    var counterKey = BuildMonthlyQueryCounterKey(nowUtc);
    return await usageCounterStore.GetCounterAsync(customerId, counterKey, cancellationToken)
        ?? new UsageCounterRecord(
            customerId,
            counterKey,
            0,
            BuildMonthlyQueryCounterResetAt(nowUtc),
            nowUtc);
}

async Task<(UsageCounterRecord Counter, IResult? ErrorResult)> ConsumeHostedQueryQuotaAsync(
    string customerId,
    EffectiveBillingState billingState,
    CancellationToken cancellationToken)
{
    var plan = ResolvePlanEntitlements(billingState.PlanId);
    var nowUtc = DateTimeOffset.UtcNow;
    var resetAt = BuildMonthlyQueryCounterResetAt(nowUtc);
    var counterKey = BuildMonthlyQueryCounterKey(nowUtc);

    if (plan.McpQueriesPerMonth < 0)
    {
        var current = await usageCounterStore.GetCounterAsync(customerId, counterKey, cancellationToken)
            ?? new UsageCounterRecord(customerId, counterKey, 0, resetAt, nowUtc);
        return (current, null);
    }

    var incremented = await usageCounterStore.IncrementCounterAsync(
        new UsageCounterIncrementRequest(
            customerId,
            counterKey,
            1,
            resetAt),
        cancellationToken);

    if (incremented.Value > plan.McpQueriesPerMonth)
    {
        return (incremented, BuildMcpQuotaExceededResult(billingState.PlanId, incremented.Value, plan, resetAt));
    }

    return (incremented, null);
}

string[] NormalizeRequestedTokenScopes(IReadOnlyList<string>? requestedScopes, PlanEntitlements plan)
{
    var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mcp:read",
    };

    foreach (var scope in requestedScopes ?? [])
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            continue;
        }

        var normalized = scope.Trim().ToLowerInvariant();
        if (normalized is not "mcp:read" and not "mcp:write")
        {
            throw new InvalidOperationException($"Unsupported token scope '{scope}'.");
        }

        scopes.Add(normalized);
    }

    if (scopes.Contains("mcp:write", StringComparer.OrdinalIgnoreCase) && !plan.McpWrite)
    {
        throw new UnauthorizedAccessException("Your current plan does not allow the 'mcp:write' scope.");
    }

    return scopes
        .OrderBy(scope => string.Equals(scope, "mcp:read", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .ToArray();
}

async Task ProcessStripeEventAsync(Event stripeEvent, string payload, CancellationToken cancellationToken)
{
    switch (stripeEvent.Type)
    {
        case StripeCheckoutSessionCompleted:
        {
            if (stripeEvent.Data.Object is not CheckoutSession checkoutSession)
            {
                return;
            }

            var customerId = await ResolveCustomerIdAsync(checkoutSession.CustomerId, checkoutSession.Metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return;
            }

            var recorded = await subscriptionStore.TryRecordSubscriptionEventAsync(
                new SubscriptionEventRecord(
                    $"subevt_{Guid.NewGuid():N}",
                    customerId,
                    stripeEvent.Id,
                    stripeEvent.Type,
                    payload,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            if (!recorded)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(checkoutSession.CustomerId))
            {
                await subscriptionStore.LinkStripeCustomerAsync(customerId, checkoutSession.CustomerId, cancellationToken);
            }

            var planId =
                checkoutSession.Metadata?.TryGetValue("planId", out var sessionPlanId) == true && !string.IsNullOrWhiteSpace(sessionPlanId)
                    ? sessionPlanId
                    : "free";

            await subscriptionStore.UpsertSubscriptionAsync(
                new SubscriptionUpsertRequest(
                    customerId,
                    planId,
                    "active",
                    checkoutSession.CustomerId,
                    checkoutSession.SubscriptionId,
                    1,
                    null,
                    null,
                    false),
                cancellationToken);

            await subscriptionStore.MarkSubscriptionEventProcessedAsync(stripeEvent.Id, cancellationToken);
            return;
        }

        case StripeCustomerSubscriptionCreated:
        case StripeCustomerSubscriptionUpdated:
        case StripeCustomerSubscriptionDeleted:
        {
            if (stripeEvent.Data.Object is not Stripe.Subscription subscription)
            {
                return;
            }

            var customerId = await ResolveCustomerIdAsync(subscription.CustomerId, subscription.Metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return;
            }

            var recorded = await subscriptionStore.TryRecordSubscriptionEventAsync(
                new SubscriptionEventRecord(
                    $"subevt_{Guid.NewGuid():N}",
                    customerId,
                    stripeEvent.Id,
                    stripeEvent.Type,
                    payload,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            if (!recorded)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(subscription.CustomerId))
            {
                await subscriptionStore.LinkStripeCustomerAsync(customerId, subscription.CustomerId, cancellationToken);
            }

            var stripePriceId = subscription.Items.Data.FirstOrDefault()?.Price?.Id;
            var planId =
                ResolvePlanIdFromStripePriceId(stripePriceId)
                ?? (subscription.Metadata?.TryGetValue("planId", out var metadataPlanId) == true ? metadataPlanId : null)
                ?? "free";
            var (currentPeriodStart, currentPeriodEnd) = ResolveSubscriptionPeriod(subscription);

            await subscriptionStore.UpsertSubscriptionAsync(
                new SubscriptionUpsertRequest(
                    customerId,
                    planId,
                    HostedBillingStateResolver.NormalizeSubscriptionStatus(subscription.Status),
                    subscription.CustomerId,
                    subscription.Id,
                    checked((int)subscription.Items.Data.Sum(item => item.Quantity)),
                    currentPeriodStart,
                    currentPeriodEnd,
                    subscription.CancelAtPeriodEnd),
                cancellationToken);

            await subscriptionStore.MarkSubscriptionEventProcessedAsync(stripeEvent.Id, cancellationToken);
            return;
        }

        case StripeInvoicePaymentFailed:
        case StripeInvoicePaymentSucceeded:
        {
            if (stripeEvent.Data.Object is not Invoice invoice)
            {
                return;
            }

            var customerId = await ResolveCustomerIdAsync(invoice.CustomerId, invoice.Metadata, cancellationToken);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                return;
            }

            var recorded = await subscriptionStore.TryRecordSubscriptionEventAsync(
                new SubscriptionEventRecord(
                    $"subevt_{Guid.NewGuid():N}",
                    customerId,
                    stripeEvent.Id,
                    stripeEvent.Type,
                    payload,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            if (!recorded)
            {
                return;
            }

            var existing = await subscriptionStore.GetSubscriptionAsync(customerId, cancellationToken)
                ?? await subscriptionStore.EnsureFreeSubscriptionAsync(customerId, cancellationToken);
            var currentPeriodStart = ToUtcDateTimeOffset(invoice.PeriodStart) ?? existing.CurrentPeriodStart;
            var currentPeriodEnd = ToUtcDateTimeOffset(invoice.PeriodEnd) ?? existing.CurrentPeriodEnd;

            await subscriptionStore.UpsertSubscriptionAsync(
                new SubscriptionUpsertRequest(
                    customerId,
                    existing.PlanId,
                    stripeEvent.Type == StripeInvoicePaymentFailed ? "past_due" : "active",
                    invoice.CustomerId,
                    existing.StripeSubscriptionId,
                    existing.SeatCount,
                    currentPeriodStart,
                    currentPeriodEnd,
                    existing.CancelAtPeriodEnd),
                cancellationToken);

            await subscriptionStore.MarkSubscriptionEventProcessedAsync(stripeEvent.Id, cancellationToken);
            return;
        }
    }
}

if (hostedAuthConfigured)
{
    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(jwt =>
        {
            jwt.Authority = options.HostedAuth.Authority;
            jwt.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.HostedAuth.Authority,
                ValidateAudience = true,
                ValidAudience = options.HostedAuth.FirebaseProjectId,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                NameClaimType = "name",
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddSingleton<ITenantCatalogStore>(tenantCatalogStore);
}

// ---------------------------------------------------------------------------
// Rate limiting policies
// ---------------------------------------------------------------------------

builder.Services.AddRateLimiter(rateLimiter =>
{
    rateLimiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict limit for token creation (5 per minute per user)
    rateLimiter.AddPolicy("token-creation", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
            }));

    // Moderate limit for tenant API (100 per minute per user)
    rateLimiter.AddPolicy("tenant-api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
            }));

    // Webhook limit (50 per minute - defensive against replay)
    rateLimiter.AddPolicy("webhooks", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 50,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    if (string.Equals(path, "/admin", StringComparison.Ordinal))
    {
        context.Response.Redirect("/admin/", permanent: false);
        return;
    }

    if (string.Equals(path, "/browse", StringComparison.Ordinal))
    {
        context.Response.Redirect("/browse/", permanent: false);
        return;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

if (hostedAuthConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new
{
    service = "OpenCortex.Api",
    status = validationErrors.Count == 0 ? "ready" : "configuration-invalid",
}));

app.MapGet("/health", () => Results.Ok(new
{
    service = "OpenCortex.Api",
    validationErrors,
}));

app.MapPost("/webhooks/stripe", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    if (!stripeConfigured)
    {
        return Results.Problem(
            title: "Stripe webhook not configured",
            detail: "Stripe billing is disabled or missing required secrets.",
            statusCode: StatusCodes.Status501NotImplemented);
    }

    // Limit webhook payload to 64KB (Stripe payloads are typically small)
    const int maxPayloadSize = 64 * 1024;
    if (request.ContentLength > maxPayloadSize)
    {
        return Results.BadRequest(new { message = "Webhook payload too large." });
    }

    request.EnableBuffering();
    using var reader = new StreamReader(request.Body, leaveOpen: true);
    var payload = await reader.ReadToEndAsync(cancellationToken);

    if (payload.Length > maxPayloadSize)
    {
        return Results.BadRequest(new { message = "Webhook payload too large." });
    }
    request.Body.Position = 0;

    var signature = request.Headers["Stripe-Signature"].ToString();
    if (string.IsNullOrWhiteSpace(signature))
    {
        return Results.BadRequest(new { message = "Stripe-Signature header is required." });
    }

    Event stripeEvent;
    try
    {
        stripeEvent = EventUtility.ConstructEvent(payload, signature, options.Billing.Stripe.WebhookSecret);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = $"Invalid Stripe webhook signature: {ex.Message}" });
    }

    await ProcessStripeEventAsync(stripeEvent, payload, cancellationToken);
    return Results.Ok(new { received = true, eventId = stripeEvent.Id, eventType = stripeEvent.Type });
}).RequireRateLimiting("webhooks");

app.MapGet("/brains", async (CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
    return Results.Ok(brains);
});

app.MapGet("/admin/brains/health", async (CancellationToken cancellationToken) =>
{
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var brains = await brainCatalogStore.ListBrainsAsync(cancellationToken);
    var recentRuns = await indexRunStore.ListIndexRunsAsync(limit: 200, cancellationToken: cancellationToken);

    var configuredBrainIds = options.Brains
        .Select(b => b.BrainId)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    var summaries = brains.Select(brain =>
    {
        var brainRuns = recentRuns
            .Where(run => string.Equals(run.BrainId, brain.BrainId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(run => run.StartedAt)
            .ToList();
        var latestRun = brainRuns.FirstOrDefault();
        var failedRunCount = brainRuns.Count(run => string.Equals(run.Status, "failed", StringComparison.OrdinalIgnoreCase));
        var runningRunCount = brainRuns.Count(run => string.Equals(run.Status, "running", StringComparison.OrdinalIgnoreCase));
        var completedRunCount = brainRuns.Count(run => string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase));

        return new BrainHealthSummary(
            brain.BrainId,
            brain.Name,
            brain.Slug,
            brain.Mode,
            brain.Status,
            brain.SourceRootCount,
            configuredBrainIds.Contains(brain.BrainId),
            latestRun?.Status ?? "never-run",
            latestRun?.StartedAt,
            latestRun?.CompletedAt,
            latestRun?.DocumentsSeen,
            latestRun?.DocumentsIndexed,
            latestRun?.DocumentsFailed,
            latestRun is not null && string.Equals(latestRun.Status, "running", StringComparison.OrdinalIgnoreCase),
            failedRunCount,
            runningRunCount,
            completedRunCount,
            latestRun?.ErrorSummary);
    });

    return Results.Ok(summaries);
});

// Admin CRUD: brains

app.MapGet("/admin/brains/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.GetBrainAsync(brainId, cancellationToken);
    return brain is null
        ? Results.NotFound(new { message = $"Brain '{brainId}' was not found." })
        : Results.Ok(brain);
});

app.MapPost("/admin/brains", async (CreateBrainRequest request, CancellationToken cancellationToken) =>
{
    var definition = new OpenCortex.Core.Brains.BrainDefinition
    {
        BrainId = request.BrainId,
        Name = request.Name,
        Slug = request.Slug,
        Mode = Enum.TryParse<OpenCortex.Core.Brains.BrainMode>(request.Mode, ignoreCase: true, out var mode)
            ? mode
            : OpenCortex.Core.Brains.BrainMode.Filesystem,
        CustomerId = request.CustomerId,
        Status = request.Status ?? "active",
    };

    var brain = await brainCatalogStore.CreateBrainAsync(definition, cancellationToken);
    return Results.Created($"/admin/brains/{brain.BrainId}", brain);
});

app.MapPut("/admin/brains/{brainId}", async (string brainId, UpdateBrainRequest request, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.UpdateBrainAsync(
        brainId,
        request.Name,
        request.Slug,
        request.Mode,
        request.Status,
        request.Description,
        cancellationToken);

    return brain is null
        ? Results.NotFound(new { message = $"Brain '{brainId}' was not found." })
        : Results.Ok(brain);
});

app.MapDelete("/admin/brains/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var retired = await brainCatalogStore.RetireBrainAsync(brainId, cancellationToken);
    return retired
        ? Results.Ok(new { message = $"Brain '{brainId}' has been retired." })
        : Results.NotFound(new { message = $"Brain '{brainId}' was not found or is already retired." });
});

// Admin CRUD: source roots

app.MapPost("/admin/brains/{brainId}/source-roots", async (string brainId, AddSourceRootRequest request, CancellationToken cancellationToken) =>
{
    var brain = await brainCatalogStore.GetBrainAsync(brainId, cancellationToken);
    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var definition = new OpenCortex.Core.Brains.SourceRootDefinition
    {
        SourceRootId = request.SourceRootId,
        Path = request.Path,
        PathType = request.PathType ?? "local",
        IsWritable = request.IsWritable,
        IncludePatterns = request.IncludePatterns ?? ["**/*.md"],
        ExcludePatterns = request.ExcludePatterns ?? [],
        WatchMode = request.WatchMode ?? "scheduled",
    };

    var sourceRoot = await brainCatalogStore.AddSourceRootAsync(brainId, definition, cancellationToken);
    return Results.Created($"/admin/brains/{brainId}/source-roots/{sourceRoot.SourceRootId}", sourceRoot);
});

app.MapPut("/admin/brains/{brainId}/source-roots/{sourceRootId}", async (string brainId, string sourceRootId, UpdateSourceRootRequest request, CancellationToken cancellationToken) =>
{
    var sourceRoot = await brainCatalogStore.UpdateSourceRootAsync(
        brainId,
        sourceRootId,
        request.Path,
        request.PathType ?? "local",
        request.IsWritable,
        request.IncludePatterns ?? ["**/*.md"],
        request.ExcludePatterns ?? [],
        request.WatchMode ?? "scheduled",
        cancellationToken);

    return sourceRoot is null
        ? Results.NotFound(new { message = $"Source root '{sourceRootId}' was not found on brain '{brainId}'." })
        : Results.Ok(sourceRoot);
});

app.MapDelete("/admin/brains/{brainId}/source-roots/{sourceRootId}", async (string brainId, string sourceRootId, CancellationToken cancellationToken) =>
{
    var removed = await brainCatalogStore.RemoveSourceRootAsync(brainId, sourceRootId, cancellationToken);
    return removed
        ? Results.Ok(new { message = $"Source root '{sourceRootId}' has been removed from brain '{brainId}'." })
        : Results.NotFound(new { message = $"Source root '{sourceRootId}' was not found on brain '{brainId}'." });
});

app.MapGet("/indexing/plans", () => Results.Ok(new BrainIndexingPlanner().BuildPlans(options)));

app.MapGet("/indexing/preview/{brainId}", async (string brainId) =>
{
    var brain = options.Brains.FirstOrDefault(candidate => string.Equals(candidate.BrainId, brainId, StringComparison.OrdinalIgnoreCase));

    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain);

    return Results.Ok(new
    {
        batch.BrainId,
        documentCount = batch.Documents.Count,
        chunkCount = batch.Chunks.Count,
        linkEdgeCount = batch.LinkEdges.Count,
        documents = batch.Documents.Select(document => new
        {
            document.DocumentId,
            document.CanonicalPath,
            document.Title,
            document.DocumentType,
        }),
    });
});

app.MapPost("/indexing/run/{brainId}", async (string brainId, CancellationToken cancellationToken) =>
{
    var brain = options.Brains.FirstOrDefault(candidate => string.Equals(candidate.BrainId, brainId, StringComparison.OrdinalIgnoreCase));

    if (brain is null)
    {
        return Results.NotFound(new { message = $"Brain '{brainId}' was not found." });
    }

    var coordinator = new BrainIngestionPersistenceCoordinator(
        new PostgresDocumentCatalogStore(connectionFactory),
        new PostgresChunkStore(connectionFactory),
        new PostgresLinkGraphStore(connectionFactory),
        indexRunStore,
        new PostgresEmbeddingStore(connectionFactory));
    await brainCatalogStore.UpsertBrainsAsync(options.Brains, cancellationToken);
    var batch = await new FilesystemBrainIngestionService(embeddingProvider).IngestAsync(brain, cancellationToken);
    var indexRun = await coordinator.PersistAsync(batch, "manual-api", cancellationToken);

    return Results.Ok(new
    {
        indexRun.IndexRunId,
        indexRun.Status,
        batch.BrainId,
        documentCount = batch.Documents.Count,
        chunkCount = batch.Chunks.Count,
        linkEdgeCount = batch.LinkEdges.Count,
    });
});

app.MapGet("/indexing/runs", async (string? brainId, int? limit, CancellationToken cancellationToken) =>
{
    var runs = await indexRunStore.ListIndexRunsAsync(brainId, limit ?? 20, cancellationToken);
    return Results.Ok(runs);
});

app.MapGet("/indexing/runs/{indexRunId}", async (string indexRunId, CancellationToken cancellationToken) =>
{
    var run = await indexRunStore.GetIndexRunAsync(indexRunId, cancellationToken);

    return run is null
        ? Results.NotFound(new { message = $"Index run '{indexRunId}' was not found." })
        : Results.Ok(run);
});

app.MapGet("/indexing/runs/{indexRunId}/errors", async (string indexRunId, CancellationToken cancellationToken) =>
{
    var run = await indexRunStore.GetIndexRunAsync(indexRunId, cancellationToken);

    if (run is null)
    {
        return Results.NotFound(new { message = $"Index run '{indexRunId}' was not found." });
    }

    var errors = await indexRunStore.ListIndexRunErrorsAsync(indexRunId, cancellationToken);
    return Results.Ok(errors);
});

app.MapPost("/query", async (OqlQueryRequest request, CancellationToken cancellationToken) =>
{
    var executor = new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider));
    var result = await executor.ExecuteAsync(request.Oql, cancellationToken);
    return Results.Ok(result);
});

if (hostedAuthConfigured)
{
    var tenantRoutes = app.MapGroup("/tenant")
        .RequireAuthorization()
        .RequireRateLimiting("tenant-api");

    tenantRoutes.MapGet("/me", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        return Results.Ok(context);
    });

    tenantRoutes.MapGet("/brains", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brains = await brainCatalogStore.ListBrainsByCustomerAsync(context!.CustomerId, cancellationToken);
        return Results.Ok(new
        {
            context.CustomerId,
            count = brains.Count,
            brains,
        });
    });

    tenantRoutes.MapGet("/billing/plan", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var activeDocumentCounter = await SyncActiveDocumentCounterAsync(context!.CustomerId, cancellationToken);
        var billingState = await GetEffectiveBillingStateAsync(context.CustomerId, cancellationToken);
        var plan = ResolvePlanEntitlements(billingState.PlanId);
        var maxDocuments = plan.MaxDocuments;
        var remainingDocuments = maxDocuments < 0 ? -1 : Math.Max(maxDocuments - (int)activeDocumentCounter.Value, 0);
        var monthlyQueryUsage = await GetMonthlyQueryUsageAsync(context.CustomerId, cancellationToken);
        var mcpQueriesRemaining = plan.McpQueriesPerMonth < 0
            ? -1
            : Math.Max(plan.McpQueriesPerMonth - monthlyQueryUsage.Value, 0);

        return Results.Ok(new
        {
            planId = billingState.PlanId,
            subscriptionPlanId = billingState.StoredPlanId,
            subscriptionStatus = billingState.SubscriptionStatus,
            currentPeriodEnd = billingState.CurrentPeriodEnd,
            cancelAtPeriodEnd = billingState.CancelAtPeriodEnd,
            isDowngradedToFree = billingState.IsDowngradedToFree,
            activeDocuments = activeDocumentCounter.Value,
            maxDocuments,
            remainingDocuments,
            maxBrains = plan.MaxBrains,
            mcpQueriesPerMonth = plan.McpQueriesPerMonth,
            mcpQueriesUsed = monthlyQueryUsage.Value,
            mcpQueriesRemaining,
            mcpQueriesResetAt = monthlyQueryUsage.ResetAt,
            mcpWrite = plan.McpWrite,
        });
    });

    tenantRoutes.MapPost("/billing/upgrade", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (!stripeConfigured || !options.Billing.Stripe.PriceIds.TryGetValue("pro", out var proPriceId) || string.IsNullOrWhiteSpace(proPriceId))
        {
            return Results.Problem(
                title: "Stripe Checkout not configured",
                detail: "Stripe billing is disabled or the Pro price ID is not configured.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var billingState = await GetEffectiveBillingStateAsync(context!.CustomerId, cancellationToken);
        var billingProfile = await subscriptionStore.GetCustomerBillingProfileAsync(context.CustomerId, cancellationToken)
            ?? new CustomerBillingProfile(context.CustomerId, null, context.PlanId, context.SubscriptionStatus, null, context.CurrentPeriodEnd, context.CancelAtPeriodEnd);

        if (!string.Equals(billingState.PlanId, HostedBillingStateResolver.FreePlanId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict(new { message = $"Workspace is already on plan '{billingState.PlanId}'." });
        }

        var sessionOptions = new CheckoutSessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = BuildAppUrl("billing/success?session_id={CHECKOUT_SESSION_ID}"),
            CancelUrl = BuildAppUrl("billing"),
            ClientReferenceId = context.CustomerId,
            Metadata = new Dictionary<string, string>
            {
                ["customerId"] = context.CustomerId,
                ["planId"] = "pro",
            },
            LineItems =
            [
                new CheckoutSessionLineItemOptions
                {
                    Price = proPriceId,
                    Quantity = 1,
                },
            ],
        };

        if (!string.IsNullOrWhiteSpace(billingProfile.StripeCustomerId))
        {
            sessionOptions.Customer = billingProfile.StripeCustomerId;
        }
        else
        {
            sessionOptions.CustomerEmail = context.Email;
        }

        var session = await new CheckoutSessionService().CreateAsync(sessionOptions, cancellationToken: cancellationToken);
        return Results.Ok(new
        {
            url = session.Url,
            sessionId = session.Id,
        });
    });

    tenantRoutes.MapPost("/billing/portal", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (!stripeConfigured)
        {
            return Results.Problem(
                title: "Stripe Customer Portal not configured",
                detail: "Stripe billing is disabled or missing required secrets.",
                statusCode: StatusCodes.Status501NotImplemented);
        }

        var billingProfile = await subscriptionStore.GetCustomerBillingProfileAsync(context!.CustomerId, cancellationToken)
            ?? new CustomerBillingProfile(context.CustomerId, null, context.PlanId, context.SubscriptionStatus, null, context.CurrentPeriodEnd, context.CancelAtPeriodEnd);

        if (string.IsNullOrWhiteSpace(billingProfile.StripeCustomerId))
        {
            return Results.Conflict(new { message = "No Stripe customer is linked to this workspace yet." });
        }

        var portalSession = await new BillingPortalSessionService().CreateAsync(
            new BillingPortalSessionCreateOptions
            {
                Customer = billingProfile.StripeCustomerId,
                ReturnUrl = BuildAppUrl("billing"),
            },
            cancellationToken: cancellationToken);

        return Results.Ok(new
        {
            url = portalSession.Url,
        });
    });

    tenantRoutes.MapGet("/tokens", async (
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var tokens = await apiTokenStore.ListTokensAsync(context!.UserId, context.CustomerId, cancellationToken);
        return Results.Ok(new
        {
            count = tokens.Count,
            tokens,
        });
    });

    tenantRoutes.MapPost("/tokens", async (
        CreateApiTokenRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.BadRequest(new { message = "Token name is required." });
        }

        var billingState = await GetEffectiveBillingStateAsync(context!.CustomerId, cancellationToken);
        var plan = ResolvePlanEntitlements(billingState.PlanId);

        string[] scopes;
        try
        {
            scopes = NormalizeRequestedTokenScopes(request.Scopes, plan);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Results.Json(
                new
                {
                    type = "forbidden",
                    title = "Insufficient plan for requested token scope",
                    detail = ex.Message,
                    requiredScope = "mcp:write",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var generatedToken = PersonalApiToken.Generate();
        var createdToken = await apiTokenStore.CreateTokenAsync(
            new ApiTokenCreateRequest(
                context.UserId,
                context.CustomerId,
                request.Name,
                generatedToken.TokenHash,
                generatedToken.TokenPrefix,
                scopes,
                request.ExpiresAt),
            cancellationToken);

        return Results.Created(
            $"/tenant/tokens/{createdToken.ApiTokenId}",
            new
            {
                createdToken.ApiTokenId,
                createdToken.Name,
                createdToken.TokenPrefix,
                createdToken.Scopes,
                createdToken.ExpiresAt,
                createdToken.CreatedAt,
                token = generatedToken.RawToken,
            });
    }).RequireRateLimiting("token-creation");

    tenantRoutes.MapDelete("/tokens/{apiTokenId}", async (
        string apiTokenId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var revoked = await apiTokenStore.RevokeTokenAsync(apiTokenId, context!.UserId, context.CustomerId, cancellationToken);
        return revoked
            ? Results.Ok(new { message = $"Token '{apiTokenId}' was revoked." })
            : Results.NotFound(new { message = $"Token '{apiTokenId}' was not found." });
    });

    tenantRoutes.MapGet("/brains/{brainId}", async (
        string brainId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);

        return brain is null
            ? Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." })
            : Results.Ok(brain);
    });

    tenantRoutes.MapPost("/query", async (
        OqlQueryRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        OqlQuery query;
        try
        {
            query = new OqlParser().Parse(request.Oql);
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, query.BrainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{query.BrainId}' was not found in your workspace." });
        }

        var billingState = await GetEffectiveBillingStateAsync(context.CustomerId, cancellationToken);
        var (_, quotaError) = await ConsumeHostedQueryQuotaAsync(context.CustomerId, billingState, cancellationToken);
        if (quotaError is not null)
        {
            return quotaError;
        }

        var executor = new OqlQueryExecutor(new PostgresDocumentQueryStore(connectionFactory, embeddingProvider));
        var result = await executor.ExecuteAsync(request.Oql, cancellationToken);
        return Results.Ok(result);
    });

    tenantRoutes.MapGet("/brains/{brainId}/indexing/runs", async (
        string brainId,
        int? limit,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        var runs = await indexRunStore.ListIndexRunsAsync(brainId, Math.Clamp(limit ?? 10, 1, 50), cancellationToken);
        return Results.Ok(new
        {
            brainId,
            count = runs.Count,
            runs,
        });
    });

    tenantRoutes.MapPost("/brains/{brainId}/reindex", async (
        string brainId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var billingState = await GetEffectiveBillingStateAsync(context.CustomerId, cancellationToken);
        var plan = ResolvePlanEntitlements(billingState.PlanId);
        if (!plan.McpWrite)
        {
            return Results.Json(
                new
                {
                    type = "forbidden",
                    title = "Plan does not allow managed-content reindex from tenant tools",
                    detail = $"Your {billingState.PlanId} plan does not allow reindex operations from the tenant workspace.",
                },
                statusCode: StatusCodes.Status403Forbidden);
        }

        var run = await BuildManagedContentIndexingService().ReindexAsync(
            context.CustomerId,
            brainId,
            "tenant-reindex",
            cancellationToken);

        return Results.Ok(run);
    });

    tenantRoutes.MapGet("/brains/{brainId}/documents", async (
        string brainId,
        int? limit,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var documents = await managedDocumentStore.ListManagedDocumentsAsync(
            context.CustomerId,
            brainId,
            Math.Clamp(limit ?? 200, 1, 500),
            cancellationToken);

        return Results.Ok(new
        {
            brainId,
            count = documents.Count,
            documents,
        });
    });

    tenantRoutes.MapGet("/brains/{brainId}/documents/by-path", async (
        string brainId,
        HttpRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var canonicalPath = request.Query["canonicalPath"].ToString();
        if (string.IsNullOrWhiteSpace(canonicalPath))
        {
            canonicalPath = request.Query["canonical_path"].ToString();
        }

        return await TenantManagedDocumentEndpoints.GetDocumentByCanonicalPathAsync(
            context!.CustomerId,
            brainId,
            canonicalPath,
            brainCatalogStore,
            managedDocumentStore,
            cancellationToken);
    });

    tenantRoutes.MapGet("/brains/{brainId}/documents/{managedDocumentId}", async (
        string brainId,
        string managedDocumentId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var document = await managedDocumentStore.GetManagedDocumentAsync(context.CustomerId, brainId, managedDocumentId, cancellationToken);
        return document is null
            ? Results.NotFound(new { message = $"Document '{managedDocumentId}' was not found in brain '{brainId}'." })
            : Results.Ok(document);
    });

    tenantRoutes.MapGet("/brains/{brainId}/documents/{managedDocumentId}/versions", async (
        string brainId,
        string managedDocumentId,
        int? limit,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var versions = await managedDocumentStore.ListManagedDocumentVersionsAsync(
            context.CustomerId,
            brainId,
            managedDocumentId,
            limit.GetValueOrDefault(25),
            cancellationToken);

        return Results.Ok(new
        {
            managedDocumentId,
            count = versions.Count,
            versions,
        });
    });

    tenantRoutes.MapGet("/brains/{brainId}/documents/{managedDocumentId}/versions/{managedDocumentVersionId}", async (
        string brainId,
        string managedDocumentId,
        string managedDocumentVersionId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var version = await managedDocumentStore.GetManagedDocumentVersionAsync(
            context.CustomerId,
            brainId,
            managedDocumentId,
            managedDocumentVersionId,
            cancellationToken);

        return version is null
            ? Results.NotFound(new { message = $"Version '{managedDocumentVersionId}' was not found for document '{managedDocumentId}'." })
            : Results.Ok(version);
    });

    tenantRoutes.MapPost("/brains/{brainId}/documents", async (
        string brainId,
        CreateManagedDocumentRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { message = "Title is required." });
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var billingState = await GetEffectiveBillingStateAsync(context.CustomerId, cancellationToken);
        var plan = ResolvePlanEntitlements(billingState.PlanId);
        if (plan.MaxDocuments >= 0)
        {
            var activeDocuments = await managedDocumentStore.CountActiveManagedDocumentsAsync(context.CustomerId, cancellationToken);
            if (activeDocuments >= plan.MaxDocuments)
            {
                return BuildQuotaExceededResult(billingState.PlanId, activeDocuments, plan);
            }
        }

        try
        {
            var document = await managedDocumentStore.CreateManagedDocumentAsync(
                new OpenCortex.Core.Persistence.ManagedDocumentCreateRequest(
                    BrainId: brainId,
                    CustomerId: context.CustomerId,
                    Title: request.Title,
                    Slug: request.Slug,
                    Content: request.Content ?? string.Empty,
                    Frontmatter: request.Frontmatter ?? new Dictionary<string, string>(),
                    Status: request.Status ?? "draft",
                    UserId: context.UserId),
                cancellationToken);

            await BuildManagedContentIndexingService().ReindexAsync(
                context.CustomerId,
                brainId,
                "managed-document-create",
                cancellationToken);

            await SyncActiveDocumentCounterAsync(context.CustomerId, cancellationToken);

            return Results.Created($"/tenant/brains/{brainId}/documents/{document.ManagedDocumentId}", document);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    });

    tenantRoutes.MapPut("/brains/{brainId}/documents/{managedDocumentId}", async (
        string brainId,
        string managedDocumentId,
        UpdateManagedDocumentRequest request,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.BadRequest(new { message = "Title is required." });
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        try
        {
            var document = await managedDocumentStore.UpdateManagedDocumentAsync(
                new OpenCortex.Core.Persistence.ManagedDocumentUpdateRequest(
                    ManagedDocumentId: managedDocumentId,
                    BrainId: brainId,
                    CustomerId: context.CustomerId,
                    Title: request.Title,
                    Slug: request.Slug,
                    Content: request.Content ?? string.Empty,
                    Frontmatter: request.Frontmatter ?? new Dictionary<string, string>(),
                    Status: request.Status ?? "draft",
                    UserId: context.UserId),
                cancellationToken);

            if (document is null)
            {
                return Results.NotFound(new { message = $"Document '{managedDocumentId}' was not found in brain '{brainId}'." });
            }

            await BuildManagedContentIndexingService().ReindexAsync(
                context.CustomerId,
                brainId,
                "managed-document-update",
                cancellationToken);

            return Results.Ok(document);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    });

    tenantRoutes.MapDelete("/brains/{brainId}/documents/{managedDocumentId}", async (
        string brainId,
        string managedDocumentId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        var deleted = await managedDocumentStore.SoftDeleteManagedDocumentAsync(
            context.CustomerId,
            brainId,
            managedDocumentId,
            context.UserId,
            cancellationToken);

        if (!deleted)
        {
            return Results.NotFound(new { message = $"Document '{managedDocumentId}' was not found in brain '{brainId}'." });
        }

        await BuildManagedContentIndexingService().ReindexAsync(
            context.CustomerId,
            brainId,
            "managed-document-delete",
            cancellationToken);

        await SyncActiveDocumentCounterAsync(context.CustomerId, cancellationToken);

        return Results.Ok(new { message = $"Document '{managedDocumentId}' was deleted." });
    });

    tenantRoutes.MapPost("/brains/{brainId}/documents/{managedDocumentId}/versions/{managedDocumentVersionId}/restore", async (
        string brainId,
        string managedDocumentId,
        string managedDocumentVersionId,
        System.Security.Claims.ClaimsPrincipal user,
        ITenantCatalogStore catalogStore,
        CancellationToken cancellationToken) =>
    {
        var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
        if (errorResult is not null)
        {
            return errorResult;
        }

        var brain = await brainCatalogStore.GetBrainByCustomerAsync(context!.CustomerId, brainId, cancellationToken);
        if (brain is null)
        {
            return Results.NotFound(new { message = $"Brain '{brainId}' was not found in your workspace." });
        }

        if (!string.Equals(brain.Mode, "managed-content", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = $"Brain '{brainId}' is not a managed-content brain." });
        }

        try
        {
            var restored = await managedDocumentStore.RestoreManagedDocumentVersionAsync(
                context.CustomerId,
                brainId,
                managedDocumentId,
                managedDocumentVersionId,
                context.UserId,
                cancellationToken);

            if (restored is null)
            {
                return Results.NotFound(new { message = $"Version '{managedDocumentVersionId}' was not found for document '{managedDocumentId}'." });
            }

            await BuildManagedContentIndexingService().ReindexAsync(
                context.CustomerId,
                brainId,
                "managed-document-restore",
                cancellationToken);

            await SyncActiveDocumentCounterAsync(context.CustomerId, cancellationToken);

            return Results.Ok(restored);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    });
}

// ---------------------------------------------------------------------------
// Browse API — document listing for authoring surface
// ---------------------------------------------------------------------------

app.MapGet("/browse/brains/{brainId}/documents", async (
    string brainId,
    string? sourceRootId,
    string? pathPrefix,
    int? limit,
    CancellationToken cancellationToken) =>
{
    var documentCatalogStore = new PostgresDocumentCatalogStore(connectionFactory);
    var documents = await documentCatalogStore.ListDocumentsAsync(
        brainId,
        sourceRootId,
        pathPrefix,
        limit ?? 200,
        cancellationToken);
    return Results.Ok(new { brainId, count = documents.Count, documents });
});

app.Run();

internal sealed record OqlQueryRequest(string Oql);

internal sealed record CreateBrainRequest(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string? CustomerId,
    string? Status);

internal sealed record UpdateBrainRequest(
    string Name,
    string Slug,
    string Mode,
    string Status,
    string? Description);

internal sealed record AddSourceRootRequest(
    string SourceRootId,
    string Path,
    string? PathType,
    bool IsWritable,
    string[]? IncludePatterns,
    string[]? ExcludePatterns,
    string? WatchMode);

internal sealed record UpdateSourceRootRequest(
    string Path,
    string? PathType,
    bool IsWritable,
    string[]? IncludePatterns,
    string[]? ExcludePatterns,
    string? WatchMode);

internal sealed record CreateManagedDocumentRequest(
    string Title,
    string? Slug,
    string? Content,
    Dictionary<string, string>? Frontmatter,
    string? Status);

internal sealed record UpdateManagedDocumentRequest(
    string Title,
    string? Slug,
    string? Content,
    Dictionary<string, string>? Frontmatter,
    string? Status);

internal sealed record CreateApiTokenRequest(
    string Name,
    IReadOnlyList<string>? Scopes,
    DateTimeOffset? ExpiresAt);

internal sealed record BrainHealthSummary(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string Status,
    int SourceRootCount,
    bool IsConfigured,
    string LatestRunStatus,
    DateTimeOffset? LatestRunStartedAt,
    DateTimeOffset? LatestRunCompletedAt,
    int? LatestDocumentsSeen,
    int? LatestDocumentsIndexed,
    int? LatestDocumentsFailed,
    bool IsLatestRunActive,
    int FailedRunCount,
    int RunningRunCount,
    int CompletedRunCount,
    string? LatestErrorSummary);

// Enables WebApplicationFactory access for integration tests
public partial class Program { }

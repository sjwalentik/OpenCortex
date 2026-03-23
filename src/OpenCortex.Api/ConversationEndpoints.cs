using OpenCortex.Conversations;
using OpenCortex.Core.Persistence;
using OpenCortex.Core.Tenancy;
using System.Text.Json.Nodes;

namespace OpenCortex.Api;

/// <summary>
/// Conversation management API endpoints for tenant workspace.
/// </summary>
public static class ConversationEndpoints
{
    /// <summary>
    /// Map conversation endpoints to a `/tenant/conversations` route group.
    /// </summary>
    public static void MapConversationEndpoints(this RouteGroupBuilder tenantRoutes)
    {
        // List conversations
        tenantRoutes.MapGet("", async (
            int? limit,
            int? offset,
            System.Security.Claims.ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IConversationService conversationService,
            CancellationToken cancellationToken) =>
        {
            var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
            if (errorResult is not null)
            {
                return errorResult;
            }

            var conversations = await conversationService.ListConversationsAsync(
                context!.CustomerId,
                context.UserId,
                limit ?? 50,
                offset ?? 0,
                cancellationToken);

            return Results.Ok(new
            {
                count = conversations.Count,
                conversations = conversations.Select(c => new
                {
                    conversationId = c.ConversationId,
                    title = c.Title,
                    createdAt = c.CreatedAt,
                    lastMessageAt = c.LastMessageAt,
                    status = c.Status.ToString().ToLowerInvariant()
                })
            });
        });

        // Create conversation
        tenantRoutes.MapPost("", async (
            CreateConversationRequest request,
            System.Security.Claims.ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IConversationService conversationService,
            CancellationToken cancellationToken) =>
        {
            var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
            if (errorResult is not null)
            {
                return errorResult;
            }

            var conversation = await conversationService.CreateConversationAsync(
                context!.CustomerId,
                context.UserId,
                string.IsNullOrWhiteSpace(request.BrainId) ? null : request.BrainId,
                request.Title,
                request.SystemPrompt,
                cancellationToken);

            conversation.Metadata = BuildConversationMetadata(
                conversation.Metadata,
                request.ProviderId,
                request.ModelId);

            if (conversation.Metadata is not null)
            {
                await conversationService.UpdateConversationAsync(conversation, cancellationToken);
            }

            var routing = GetConversationRoutingPreference(conversation.Metadata);

            return Results.Created($"/tenant/conversations/{conversation.ConversationId}", new
            {
                conversationId = conversation.ConversationId,
                title = conversation.Title,
                brainId = conversation.BrainId,
                providerId = routing.ProviderId,
                modelId = routing.ModelId,
                createdAt = conversation.CreatedAt,
                status = conversation.Status.ToString().ToLowerInvariant()
            });
        });

        // Get conversation with messages
        tenantRoutes.MapGet("/{conversationId}", async (
            string conversationId,
            int? messageLimit,
            System.Security.Claims.ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            CancellationToken cancellationToken) =>
        {
            var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
            if (errorResult is not null)
            {
                return errorResult;
            }

            var conversation = await conversationRepository.GetWithMessagesAsync(
                conversationId,
                messageLimit ?? 100,
                cancellationToken);

            if (!IsConversationOwnedByRequester(conversation, context))
            {
                return Results.NotFound(new { message = $"Conversation '{conversationId}' was not found." });
            }

            var ownedConversation = conversation!;
            var routing = GetConversationRoutingPreference(ownedConversation.Metadata);

            return Results.Ok(new
            {
                conversationId = ownedConversation.ConversationId,
                title = ownedConversation.Title,
                brainId = ownedConversation.BrainId,
                providerId = routing.ProviderId,
                modelId = routing.ModelId,
                systemPrompt = ownedConversation.SystemPrompt,
                createdAt = ownedConversation.CreatedAt,
                lastMessageAt = ownedConversation.LastMessageAt,
                status = ownedConversation.Status.ToString().ToLowerInvariant(),
                messages = ownedConversation.Messages?.Select(m => new
                {
                    messageId = m.MessageId,
                    role = m.Role.ToString().ToLowerInvariant(),
                    content = m.Content,
                    providerId = m.ProviderId,
                    modelId = m.ModelId,
                    latencyMs = m.LatencyMs,
                    createdAt = m.CreatedAt,
                    tokenUsage = m.GetTokenUsage()
                }) ?? []
            });
        });

        // Update conversation title
        tenantRoutes.MapPatch("/{conversationId}", async (
            string conversationId,
            UpdateConversationRequest request,
            System.Security.Claims.ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            CancellationToken cancellationToken) =>
        {
            var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
            if (errorResult is not null)
            {
                return errorResult;
            }

            var conversation = await conversationRepository.GetByIdAsync(conversationId, cancellationToken);

            if (!IsConversationOwnedByRequester(conversation, context))
            {
                return Results.NotFound(new { message = $"Conversation '{conversationId}' was not found." });
            }

            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                await conversationService.UpdateTitleAsync(conversationId, request.Title, cancellationToken);
            }

            if (request.UpdateRouting)
            {
                var ownedConversation = conversation!;
                ownedConversation.Metadata = BuildConversationMetadata(
                    ownedConversation.Metadata,
                    request.ProviderId,
                    request.ModelId);
                await conversationService.UpdateConversationAsync(ownedConversation, cancellationToken);
            }

            return Results.Ok(new { message = "Conversation updated." });
        });

        // Archive conversation
        tenantRoutes.MapDelete("/{conversationId}", async (
            string conversationId,
            System.Security.Claims.ClaimsPrincipal user,
            ITenantCatalogStore catalogStore,
            IConversationService conversationService,
            IConversationRepository conversationRepository,
            CancellationToken cancellationToken) =>
        {
            var (context, errorResult) = await HostedTenantContextResolver.ResolveAsync(user, catalogStore, cancellationToken);
            if (errorResult is not null)
            {
                return errorResult;
            }

            var conversation = await conversationRepository.GetByIdAsync(conversationId, cancellationToken);

            if (!IsConversationOwnedByRequester(conversation, context))
            {
                return Results.NotFound(new { message = $"Conversation '{conversationId}' was not found." });
            }

            await conversationService.ArchiveConversationAsync(conversationId, cancellationToken);

            return Results.Ok(new { message = $"Conversation '{conversationId}' was archived." });
        });
    }

    private static string? BuildConversationMetadata(
        string? existingMetadata,
        string? providerId,
        string? modelId)
    {
        JsonObject metadata;
        try
        {
            metadata = JsonNode.Parse(existingMetadata ?? "{}") as JsonObject ?? new JsonObject();
        }
        catch
        {
            metadata = new JsonObject();
        }

        if (string.IsNullOrWhiteSpace(providerId))
        {
            metadata.Remove("providerId");
        }
        else
        {
            metadata["providerId"] = providerId.Trim();
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            metadata.Remove("modelId");
        }
        else
        {
            metadata["modelId"] = modelId.Trim();
        }

        return metadata.Count == 0 ? null : metadata.ToJsonString();
    }

    private static ConversationRoutingPreference GetConversationRoutingPreference(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return new ConversationRoutingPreference(null, null);
        }

        try
        {
            var metadata = JsonNode.Parse(metadataJson) as JsonObject;
            return new ConversationRoutingPreference(
                metadata?["providerId"]?.GetValue<string?>(),
                metadata?["modelId"]?.GetValue<string?>());
        }
        catch
        {
            return new ConversationRoutingPreference(null, null);
        }
    }

    private static bool IsConversationOwnedByRequester(Conversation? conversation, TenantContext? context) =>
        conversation is not null
        && context is not null
        && string.Equals(conversation.CustomerId, context.CustomerId, StringComparison.Ordinal)
        && string.Equals(conversation.UserId, context.UserId, StringComparison.Ordinal);
}

internal sealed record CreateConversationRequest(
    string? Title,
    string? BrainId,
    string? SystemPrompt,
    string? ProviderId = null,
    string? ModelId = null);

internal sealed record UpdateConversationRequest(
    string? Title,
    bool UpdateRouting = false,
    string? ProviderId = null,
    string? ModelId = null);

internal sealed record ConversationRoutingPreference(
    string? ProviderId,
    string? ModelId);


using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCortex.Conversations;
using OpenCortex.Orchestration;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Api;

/// <summary>
/// Generates short conversation titles with a configured user model provider.
/// </summary>
public sealed class ModelBackedConversationTitleGenerator : IConversationTitleGenerator
{
    private static readonly string[] ProviderPreferenceOrder =
    [
        "openai",
        "anthropic",
        "openai-codex",
        "ollama",
        "ollama-remote"
    ];

    private readonly IUserProviderFactory _providerFactory;
    private readonly ILogger<ModelBackedConversationTitleGenerator> _logger;

    public ModelBackedConversationTitleGenerator(
        IUserProviderFactory providerFactory,
        ILogger<ModelBackedConversationTitleGenerator> logger)
    {
        _providerFactory = providerFactory;
        _logger = logger;
    }

    public async Task<string?> GenerateTitleAsync(
        Conversation conversation,
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseGuid(conversation.CustomerId, out var customerId)
            || !TryParseGuid(conversation.UserId, out var userId))
        {
            return null;
        }

        var provider = await ResolveProviderAsync(conversation, customerId, userId, cancellationToken);
        if (provider is null)
        {
            return null;
        }

        var titleRequest = new ChatRequest
        {
            Model = ResolveModelId(conversation.Metadata),
            Messages = BuildPromptMessages(messages),
            Options = new ChatRequestOptions
            {
                Temperature = 0.2,
                MaxTokens = 24,
                StopSequences = ["\n"]
            }
        };

        try
        {
            var completion = await provider.CompleteAsync(titleRequest, cancellationToken);
            return SanitizeTitle(completion.Content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Conversation title generation failed for conversation {ConversationId} with provider {ProviderId}",
                conversation.ConversationId,
                provider.ProviderId);
            return null;
        }
    }

    private async Task<IModelProvider?> ResolveProviderAsync(
        Conversation conversation,
        Guid customerId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var requestedProviderId = ResolveProviderId(conversation.Metadata);
        if (!string.IsNullOrWhiteSpace(requestedProviderId))
        {
            var provider = await _providerFactory.GetProviderForUserAsync(
                customerId,
                userId,
                requestedProviderId,
                cancellationToken);
            if (provider?.Capabilities.SupportsChat == true)
            {
                return provider;
            }
        }

        var providers = await _providerFactory.GetProvidersForUserAsync(customerId, userId, cancellationToken);
        return providers
            .Where(provider => provider.Capabilities.SupportsChat)
            .OrderBy(provider =>
            {
                var index = Array.IndexOf(ProviderPreferenceOrder, provider.ProviderId);
                return index < 0 ? int.MaxValue : index;
            })
            .FirstOrDefault();
    }

    private static IReadOnlyList<ChatMessage> BuildPromptMessages(IReadOnlyList<Message> messages)
    {
        var transcript = messages
            .Where(message =>
                (message.Role == MessageRole.User || message.Role == MessageRole.Assistant)
                && !string.IsNullOrWhiteSpace(message.Content))
            .Take(6)
            .Select(message => $"{message.Role}: {message.Content!.Trim()}")
            .ToList();

        return
        [
            new ChatMessage
            {
                Role = ChatRole.System,
                Content = "Write a short title for this conversation. Use 3 to 7 words. No quotes. No punctuation at the end. Return only the title."
            },
            new ChatMessage
            {
                Role = ChatRole.User,
                Content = string.Join("\n", transcript)
            }
        ];
    }

    private static bool TryParseGuid(string? value, out Guid parsed)
    {
        if (Guid.TryParse(value, out parsed))
        {
            return true;
        }

        parsed = Guid.Empty;
        return false;
    }

    private static string? ResolveProviderId(string? metadataJson)
    {
        if (!TryParseMetadata(metadataJson, out var metadata))
        {
            return null;
        }

        return metadata.TryGetProperty("providerId", out var providerId)
            ? providerId.GetString()?.Trim()
            : null;
    }

    private static string ResolveModelId(string? metadataJson)
    {
        if (!TryParseMetadata(metadataJson, out var metadata))
        {
            return string.Empty;
        }

        return metadata.TryGetProperty("modelId", out var modelId)
            ? modelId.GetString()?.Trim() ?? string.Empty
            : string.Empty;
    }

    private static bool TryParseMetadata(string? metadataJson, out JsonElement metadata)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            metadata = default;
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            metadata = document.RootElement.Clone();
            return metadata.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            metadata = default;
            return false;
        }
    }

    private static string? SanitizeTitle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var title = value.Trim()
            .Trim('"', '\'', '`')
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        title = System.Text.RegularExpressions.Regex.Replace(title, @"\s+", " ").Trim();
        title = title.TrimEnd('.', '!', '?', ':', ';', ',');

        return string.IsNullOrWhiteSpace(title) ? null : title;
    }
}

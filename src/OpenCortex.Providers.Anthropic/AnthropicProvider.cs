using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Anthropic;

/// <summary>
/// Model provider implementation for Anthropic Claude models.
/// </summary>
public sealed class AnthropicProvider : ModelProviderBase
{
    private readonly AnthropicOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AnthropicProvider(
        HttpClient httpClient,
        IOptions<AnthropicOptions> options,
        ILogger<AnthropicProvider> logger)
        : base(httpClient, logger)
    {
        _options = options.Value;
        ConfigureHttpClient();
    }

    public override string ProviderId => _options.ProviderId;
    public override string Name => _options.Name;
    public override string ProviderType => "anthropic";

    public override ProviderCapabilities Capabilities => new()
    {
        SupportsChat = true,
        SupportsCode = true,
        SupportsVision = true,
        SupportsTools = true,
        SupportsStreaming = true,
        MaxContextTokens = 200000,
        MaxOutputTokens = 8192
    };

    private void ConfigureHttpClient()
    {
        HttpClient.BaseAddress = new Uri(_options.Endpoint);
        HttpClient.DefaultRequestHeaders.Add("x-api-key", _options.ApiKey);
        HttpClient.DefaultRequestHeaders.Add("anthropic-version", _options.ApiVersion);
        HttpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public override async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var anthropicRequest = MapToAnthropicRequest(request);

        var response = await HttpClient.PostAsJsonAsync(
            "/v1/messages",
            anthropicRequest,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var anthropicResponse = await response.Content.ReadFromJsonAsync<AnthropicResponse>(
            JsonOptions,
            cancellationToken);

        return MapToCompletion(anthropicResponse!);
    }

    public override async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var anthropicRequest = MapToAnthropicRequest(request);
        anthropicRequest.Stream = true;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(anthropicRequest, JsonOptions),
                Encoding.UTF8,
                "application/json")
        };

        var response = await HttpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        var contentBuilder = new StringBuilder();
        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
        TokenUsage? finalUsage = null;
        FinishReason? finishReason = null;
        string? model = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            var streamEvent = JsonSerializer.Deserialize<AnthropicStreamEvent>(data, JsonOptions);
            if (streamEvent is null) continue;

            switch (streamEvent.Type)
            {
                case "message_start":
                    model = streamEvent.Message?.Model;
                    break;

                case "content_block_delta":
                    if (streamEvent.Delta?.Type == "text_delta")
                    {
                        var textDelta = streamEvent.Delta.Text;
                        if (!string.IsNullOrEmpty(textDelta))
                        {
                            contentBuilder.Append(textDelta);
                            yield return new StreamChunk
                            {
                                ContentDelta = textDelta,
                                Model = model
                            };
                        }
                    }
                    else if (streamEvent.Delta?.Type == "input_json_delta")
                    {
                        var index = streamEvent.Index ?? 0;
                        if (!toolCallBuilders.TryGetValue(index, out var builder))
                        {
                            builder = new ToolCallBuilder();
                            toolCallBuilders[index] = builder;
                        }
                        builder.ArgumentsBuilder.Append(streamEvent.Delta.PartialJson);

                        yield return new StreamChunk
                        {
                            ToolCallDelta = new ToolCallDelta
                            {
                                Index = index,
                                ArgumentsDelta = streamEvent.Delta.PartialJson
                            },
                            Model = model
                        };
                    }
                    break;

                case "content_block_start":
                    if (streamEvent.ContentBlock?.Type == "tool_use")
                    {
                        var index = streamEvent.Index ?? 0;
                        var builder = new ToolCallBuilder
                        {
                            Id = streamEvent.ContentBlock.Id,
                            Name = streamEvent.ContentBlock.Name
                        };
                        toolCallBuilders[index] = builder;

                        yield return new StreamChunk
                        {
                            ToolCallDelta = new ToolCallDelta
                            {
                                Index = index,
                                Id = builder.Id,
                                FunctionName = builder.Name
                            },
                            Model = model
                        };
                    }
                    break;

                case "message_delta":
                    if (streamEvent.Delta?.StopReason is not null)
                    {
                        finishReason = MapFinishReason(streamEvent.Delta.StopReason);
                    }
                    if (streamEvent.Usage is not null)
                    {
                        finalUsage = new TokenUsage
                        {
                            PromptTokens = streamEvent.Usage.InputTokens,
                            CompletionTokens = streamEvent.Usage.OutputTokens
                        };
                    }
                    break;

                case "message_stop":
                    yield return new StreamChunk
                    {
                        IsComplete = true,
                        FinalUsage = finalUsage,
                        FinishReason = finishReason ?? FinishReason.Stop,
                        Model = model
                    };
                    break;
            }
        }
    }

    public override async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        // Anthropic doesn't have a list models endpoint, return known models
        return await Task.FromResult<IReadOnlyList<ModelInfo>>(
        [
            new ModelInfo
            {
                Id = "claude-opus-4-20250514",
                Name = "Claude Opus 4",
                OwnedBy = "anthropic",
                ContextWindow = 200000,
                Capabilities = new ProviderCapabilities
                {
                    SupportsChat = true,
                    SupportsCode = true,
                    SupportsVision = true,
                    SupportsTools = true,
                    SupportsStreaming = true,
                    MaxContextTokens = 200000,
                    MaxOutputTokens = 8192
                }
            },
            new ModelInfo
            {
                Id = "claude-sonnet-4-20250514",
                Name = "Claude Sonnet 4",
                OwnedBy = "anthropic",
                ContextWindow = 200000,
                Capabilities = new ProviderCapabilities
                {
                    SupportsChat = true,
                    SupportsCode = true,
                    SupportsVision = true,
                    SupportsTools = true,
                    SupportsStreaming = true,
                    MaxContextTokens = 200000,
                    MaxOutputTokens = 8192
                }
            },
            new ModelInfo
            {
                Id = "claude-3-5-haiku-20241022",
                Name = "Claude 3.5 Haiku",
                OwnedBy = "anthropic",
                ContextWindow = 200000,
                Capabilities = new ProviderCapabilities
                {
                    SupportsChat = true,
                    SupportsCode = true,
                    SupportsVision = false,
                    SupportsTools = true,
                    SupportsStreaming = true,
                    MaxContextTokens = 200000,
                    MaxOutputTokens = 8192
                }
            }
        ]);
    }

    private AnthropicRequest MapToAnthropicRequest(ChatRequest request)
    {
        var systemMessage = request.Messages
            .FirstOrDefault(m => m.Role == ChatRole.System)?.Content;

        var messages = request.Messages
            .Where(m => m.Role != ChatRole.System)
            .Select(MapMessage)
            .ToList();

        var anthropicRequest = new AnthropicRequest
        {
            Model = request.Model,
            Messages = messages,
            System = systemMessage,
            MaxTokens = request.Options?.MaxTokens ?? _options.DefaultMaxTokens,
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            StopSequences = request.Options?.StopSequences?.ToList()
        };

        if (request.Tools?.Count > 0)
        {
            anthropicRequest.Tools = request.Tools.Select(t => new AnthropicTool
            {
                Name = t.Function.Name,
                Description = t.Function.Description,
                InputSchema = t.Function.Parameters
            }).ToList();
        }

        return anthropicRequest;
    }

    private static AnthropicMessage MapMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "user", // Tool results come as user messages in Anthropic
            _ => "user"
        };

        var content = new List<AnthropicContent>();

        if (message.Role == ChatRole.Tool && message.ToolCallId is not null)
        {
            content.Add(new AnthropicContent
            {
                Type = "tool_result",
                ToolUseId = message.ToolCallId,
                Content = message.Content
            });
        }
        else if (message.ToolCalls?.Count > 0)
        {
            foreach (var toolCall in message.ToolCalls)
            {
                content.Add(new AnthropicContent
                {
                    Type = "tool_use",
                    Id = toolCall.Id,
                    Name = toolCall.Function.Name,
                    Input = JsonSerializer.Deserialize<JsonElement>(toolCall.Function.Arguments)
                });
            }
        }
        else if (!string.IsNullOrEmpty(message.Content))
        {
            content.Add(new AnthropicContent
            {
                Type = "text",
                Text = message.Content
            });
        }

        return new AnthropicMessage
        {
            Role = role,
            Content = content
        };
    }

    private static ChatCompletion MapToCompletion(AnthropicResponse response)
    {
        var textContent = response.Content?
            .Where(c => c.Type == "text")
            .Select(c => c.Text)
            .FirstOrDefault();

        var toolCalls = response.Content?
            .Where(c => c.Type == "tool_use")
            .Select(c => new ToolCall
            {
                Id = c.Id!,
                Function = new FunctionCall
                {
                    Name = c.Name!,
                    Arguments = JsonSerializer.Serialize(c.Input)
                }
            })
            .ToList();

        return new ChatCompletion
        {
            Content = textContent,
            ToolCalls = toolCalls?.Count > 0 ? toolCalls : null,
            Usage = new TokenUsage
            {
                PromptTokens = response.Usage?.InputTokens ?? 0,
                CompletionTokens = response.Usage?.OutputTokens ?? 0
            },
            FinishReason = MapFinishReason(response.StopReason),
            Model = response.Model ?? "unknown"
        };
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder ArgumentsBuilder { get; } = new();
    }
}

#region Anthropic API DTOs

internal sealed class AnthropicRequest
{
    public required string Model { get; set; }
    public required List<AnthropicMessage> Messages { get; set; }
    public string? System { get; set; }
    public int MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public List<string>? StopSequences { get; set; }
    public List<AnthropicTool>? Tools { get; set; }
    public bool Stream { get; set; }
}

internal sealed class AnthropicMessage
{
    public required string Role { get; set; }
    public required List<AnthropicContent> Content { get; set; }
}

internal sealed class AnthropicContent
{
    public required string Type { get; set; }
    public string? Text { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
    public JsonElement? Input { get; set; }
    public string? ToolUseId { get; set; }
    public string? Content { get; set; }
}

internal sealed class AnthropicTool
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public JsonDocument? InputSchema { get; set; }
}

internal sealed class AnthropicResponse
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public string? Role { get; set; }
    public string? Model { get; set; }
    public List<AnthropicContent>? Content { get; set; }
    public string? StopReason { get; set; }
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicUsage
{
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

internal sealed class AnthropicStreamEvent
{
    public string? Type { get; set; }
    public int? Index { get; set; }
    public AnthropicStreamMessage? Message { get; set; }
    public AnthropicStreamDelta? Delta { get; set; }
    public AnthropicContentBlock? ContentBlock { get; set; }
    public AnthropicUsage? Usage { get; set; }
}

internal sealed class AnthropicStreamMessage
{
    public string? Id { get; set; }
    public string? Model { get; set; }
}

internal sealed class AnthropicStreamDelta
{
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? PartialJson { get; set; }
    public string? StopReason { get; set; }
}

internal sealed class AnthropicContentBlock
{
    public string? Type { get; set; }
    public string? Id { get; set; }
    public string? Name { get; set; }
}

#endregion

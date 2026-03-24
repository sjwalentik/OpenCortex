using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.OpenAI;

/// <summary>
/// Model provider implementation for OpenAI and compatible APIs.
/// </summary>
public sealed class OpenAIProvider : ModelProviderBase
{
    private readonly OpenAIOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAIProvider(
        HttpClient httpClient,
        IOptions<OpenAIOptions> options,
        ILogger<OpenAIProvider> logger)
        : base(httpClient, logger)
    {
        _options = options.Value;
        ConfigureHttpClient();
    }

    public override string ProviderId => _options.ProviderId;
    public override string Name => _options.Name;
    public override string ProviderType => "openai";

    public override ProviderCapabilities Capabilities => new()
    {
        SupportsChat = true,
        SupportsCode = true,
        SupportsVision = true,
        SupportsTools = true,
        SupportsStreaming = true,
        MaxContextTokens = 128000,
        MaxOutputTokens = 16384
    };

    private void ConfigureHttpClient()
    {
        HttpClient.BaseAddress = new Uri(_options.Endpoint);
        HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");

        if (!string.IsNullOrEmpty(_options.OrganizationId))
        {
            HttpClient.DefaultRequestHeaders.Add("OpenAI-Organization", _options.OrganizationId);
        }

        HttpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public override async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var openAIRequest = MapToOpenAIRequest(request);

        var response = await HttpClient.PostAsJsonAsync(
            "/chat/completions",
            openAIRequest,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var openAIResponse = await response.Content.ReadFromJsonAsync<OpenAIResponse>(
            JsonOptions,
            cancellationToken);

        return MapToCompletion(openAIResponse!);
    }

    public override async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var openAIRequest = MapToOpenAIRequest(request);
        openAIRequest.Stream = true;
        openAIRequest.StreamOptions = new OpenAIStreamOptions { IncludeUsage = true };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(openAIRequest, JsonOptions),
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

        var toolCallBuilders = new Dictionary<int, ToolCallBuilder>();
        TokenUsage? finalUsage = null;
        FinishReason? finishReason = null;
        string? model = null;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            var streamChunk = JsonSerializer.Deserialize<OpenAIStreamChunk>(data, JsonOptions);
            if (streamChunk is null) continue;

            model ??= streamChunk.Model;

            // Handle usage in final chunk
            if (streamChunk.Usage is not null)
            {
                finalUsage = new TokenUsage
                {
                    PromptTokens = streamChunk.Usage.PromptTokens,
                    CompletionTokens = streamChunk.Usage.CompletionTokens
                };
            }

            var choice = streamChunk.Choices?.FirstOrDefault();
            if (choice is null) continue;

            var delta = choice.Delta;
            if (delta is null) continue;

            // Handle content delta
            if (!string.IsNullOrEmpty(delta.Content))
            {
                yield return new StreamChunk
                {
                    ContentDelta = delta.Content,
                    Model = model
                };
            }

            // Handle tool call deltas
            if (delta.ToolCalls is not null)
            {
                foreach (var toolCallDelta in delta.ToolCalls)
                {
                    var index = toolCallDelta.Index;

                    if (!toolCallBuilders.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCallBuilders[index] = builder;
                    }

                    if (!string.IsNullOrEmpty(toolCallDelta.Id))
                        builder.Id = toolCallDelta.Id;

                    if (toolCallDelta.Function?.Name is not null)
                        builder.Name = toolCallDelta.Function.Name;

                    if (toolCallDelta.Function?.Arguments is not null)
                        builder.ArgumentsBuilder.Append(toolCallDelta.Function.Arguments);

                    yield return new StreamChunk
                    {
                        ToolCallDelta = new ToolCallDelta
                        {
                            Index = index,
                            Id = toolCallDelta.Id,
                            FunctionName = toolCallDelta.Function?.Name,
                            ArgumentsDelta = toolCallDelta.Function?.Arguments
                        },
                        Model = model
                    };
                }
            }

            // Handle finish reason
            if (!string.IsNullOrEmpty(choice.FinishReason))
            {
                finishReason = MapFinishReason(choice.FinishReason);
            }
        }

        // Emit final chunk
        yield return new StreamChunk
        {
            IsComplete = true,
            FinalUsage = finalUsage,
            FinishReason = finishReason ?? FinishReason.Stop,
            Model = model
        };
    }

    public override async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var response = await HttpClient.GetAsync("/models", cancellationToken);
        response.EnsureSuccessStatusCode();

        var modelsResponse = await response.Content.ReadFromJsonAsync<OpenAIModelsResponse>(
            JsonOptions,
            cancellationToken);

        return modelsResponse?.Data?
            .Where(m => m.Id?.StartsWith("gpt") == true || m.Id?.Contains("o1") == true || m.Id?.Contains("o3") == true)
            .Select(m => new ModelInfo
            {
                Id = m.Id!,
                Name = m.Id,
                OwnedBy = m.OwnedBy
            })
            .ToList() ?? [];
    }

    private static OpenAIRequest MapToOpenAIRequest(ChatRequest request)
    {
        var messages = request.Messages.Select(MapMessage).ToList();

        var openAIRequest = new OpenAIRequest
        {
            Model = request.Model,
            Messages = messages,
            MaxTokens = request.Options?.MaxTokens,
            Temperature = request.Options?.Temperature,
            TopP = request.Options?.TopP,
            Stop = request.Options?.StopSequences?.ToList(),
            PresencePenalty = request.Options?.PresencePenalty,
            FrequencyPenalty = request.Options?.FrequencyPenalty,
            User = request.Options?.User
        };

        if (request.Tools?.Count > 0)
        {
            openAIRequest.Tools = request.Tools.Select(t => new OpenAITool
            {
                Type = "function",
                Function = new OpenAIFunction
                {
                    Name = t.Function.Name,
                    Description = t.Function.Description,
                    Parameters = t.Function.Parameters
                }
            }).ToList();

            openAIRequest.ToolChoice = request.Options?.ToolChoice switch
            {
                Abstractions.ToolChoice.None => "none",
                Abstractions.ToolChoice.Required => "required",
                _ => "auto"
            };
        }

        return openAIRequest;
    }

    private static OpenAIMessage MapMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        var openAIMessage = new OpenAIMessage
        {
            Role = role,
            Content = message.Content,
            ToolCallId = message.ToolCallId,
            Name = message.ToolName
        };

        if (message.ToolCalls?.Count > 0)
        {
            openAIMessage.ToolCalls = message.ToolCalls.Select(tc => new OpenAIToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OpenAIFunctionCall
                {
                    Name = tc.Function.Name,
                    Arguments = tc.Function.Arguments
                }
            }).ToList();
        }

        return openAIMessage;
    }

    private static ChatCompletion MapToCompletion(OpenAIResponse response)
    {
        var choice = response.Choices?.FirstOrDefault();

        var toolCalls = choice?.Message?.ToolCalls?.Select(tc => new ToolCall
        {
            Id = tc.Id!,
            Function = new FunctionCall
            {
                Name = tc.Function!.Name!,
                Arguments = tc.Function.Arguments ?? "{}"
            }
        }).ToList();

        return new ChatCompletion
        {
            Content = choice?.Message?.Content,
            ToolCalls = toolCalls?.Count > 0 ? toolCalls : null,
            Usage = new TokenUsage
            {
                PromptTokens = response.Usage?.PromptTokens ?? 0,
                CompletionTokens = response.Usage?.CompletionTokens ?? 0
            },
            FinishReason = MapFinishReason(choice?.FinishReason),
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

#region OpenAI API DTOs

internal sealed class OpenAIRequest
{
    public required string Model { get; set; }
    public required List<OpenAIMessage> Messages { get; set; }
    public int? MaxTokens { get; set; }
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public List<string>? Stop { get; set; }
    public double? PresencePenalty { get; set; }
    public double? FrequencyPenalty { get; set; }
    public string? User { get; set; }
    public List<OpenAITool>? Tools { get; set; }
    public string? ToolChoice { get; set; }
    public bool Stream { get; set; }
    public OpenAIStreamOptions? StreamOptions { get; set; }
}

internal sealed class OpenAIStreamOptions
{
    public bool IncludeUsage { get; set; }
}

internal sealed class OpenAIMessage
{
    public required string Role { get; set; }
    public string? Content { get; set; }
    public string? Name { get; set; }
    public string? ToolCallId { get; set; }
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAITool
{
    public required string Type { get; set; }
    public required OpenAIFunction Function { get; set; }
}

internal sealed class OpenAIFunction
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public JsonDocument? Parameters { get; set; }
}

internal sealed class OpenAIToolCall
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public int Index { get; set; }
    public OpenAIFunctionCall? Function { get; set; }
}

internal sealed class OpenAIFunctionCall
{
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}

internal sealed class OpenAIResponse
{
    public string? Id { get; set; }
    public string? Object { get; set; }
    public string? Model { get; set; }
    public List<OpenAIChoice>? Choices { get; set; }
    public OpenAIUsage? Usage { get; set; }
}

internal sealed class OpenAIChoice
{
    public int Index { get; set; }
    public OpenAIMessage? Message { get; set; }
    public OpenAIMessageDelta? Delta { get; set; }
    public string? FinishReason { get; set; }
}

internal sealed class OpenAIMessageDelta
{
    public string? Role { get; set; }
    public string? Content { get; set; }
    public List<OpenAIToolCall>? ToolCalls { get; set; }
}

internal sealed class OpenAIUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

internal sealed class OpenAIStreamChunk
{
    public string? Id { get; set; }
    public string? Model { get; set; }
    public List<OpenAIChoice>? Choices { get; set; }
    public OpenAIUsage? Usage { get; set; }
}

internal sealed class OpenAIModelsResponse
{
    public List<OpenAIModelInfo>? Data { get; set; }
}

internal sealed class OpenAIModelInfo
{
    public string? Id { get; set; }
    public string? OwnedBy { get; set; }
}

#endregion

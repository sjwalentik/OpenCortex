using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Providers.Ollama;

/// <summary>
/// Model provider implementation for local Ollama models.
/// Uses the OpenAI-compatible API endpoint.
/// </summary>
public sealed class OllamaProvider : ModelProviderBase
{
    private readonly OllamaOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaProvider(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaProvider> logger)
        : base(httpClient, logger)
    {
        _options = options.Value;
        ConfigureHttpClient();
    }

    public override string ProviderId => _options.ProviderId;
    public override string Name => _options.Name;
    public override string ProviderType => "ollama";

    public override ProviderCapabilities Capabilities => new()
    {
        SupportsChat = true,
        SupportsCode = true,
        SupportsVision = false, // Most Ollama models don't support vision
        SupportsTools = true,   // Ollama supports tool calling for some models
        SupportsStreaming = true,
        MaxContextTokens = 128000,
        MaxOutputTokens = 8192
    };

    private void ConfigureHttpClient()
    {
        HttpClient.BaseAddress = new Uri(_options.Endpoint);

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            HttpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.ApiKey}");
        }

        HttpClient.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);
    }

    public override async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var ollamaRequest = MapToOllamaRequest(request);

        var response = await HttpClient.PostAsJsonAsync(
            "/v1/chat/completions",
            ollamaRequest,
            JsonOptions,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(
            JsonOptions,
            cancellationToken);

        return MapToCompletion(ollamaResponse!);
    }

    public override async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        request = ApplyDefaults(request, _options.DefaultModel);
        var ollamaRequest = MapToOllamaRequest(request);
        ollamaRequest.Stream = true;

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(ollamaRequest, JsonOptions),
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

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var data = line[6..];
            if (data == "[DONE]") break;

            var streamChunk = JsonSerializer.Deserialize<OllamaStreamChunk>(data, JsonOptions);
            if (streamChunk is null) continue;

            model ??= streamChunk.Model;

            // Handle usage
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
            FinalUsage = finalUsage ?? TokenUsage.Empty,
            FinishReason = finishReason ?? FinishReason.Stop,
            Model = model
        };
    }

    public override async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        // Use the native Ollama API to list models
        var response = await HttpClient.GetAsync("/api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();

        var modelsResponse = await response.Content.ReadFromJsonAsync<OllamaModelsResponse>(
            JsonOptions,
            cancellationToken);

        return modelsResponse?.Models?
            .Select(m => new ModelInfo
            {
                Id = m.Name ?? "unknown",
                Name = m.Name,
                OwnedBy = "local"
            })
            .ToList() ?? [];
    }

    public override async Task<ProviderHealthResult> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // Simple health check by listing models
            var response = await HttpClient.GetAsync("/api/tags", cancellationToken);
            stopwatch.Stop();

            if (response.IsSuccessStatusCode)
            {
                return ProviderHealthResult.Healthy((int)stopwatch.ElapsedMilliseconds);
            }

            return ProviderHealthResult.Unhealthy($"Status code: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Ollama health check failed");
            return ProviderHealthResult.Unhealthy(ex.Message);
        }
    }

    private OllamaRequest MapToOllamaRequest(ChatRequest request)
    {
        var messages = request.Messages.Select(MapMessage).ToList();

        var ollamaRequest = new OllamaRequest
        {
            Model = request.Model,
            Messages = messages,
            Options = new OllamaRequestOptions
            {
                Temperature = request.Options?.Temperature,
                TopP = request.Options?.TopP,
                NumPredict = request.Options?.MaxTokens ?? _options.NumPredict,
                Stop = request.Options?.StopSequences?.ToList()
            }
        };

        if (request.Tools?.Count > 0)
        {
            ollamaRequest.Tools = request.Tools.Select(t => new OllamaTool
            {
                Type = "function",
                Function = new OllamaFunction
                {
                    Name = t.Function.Name,
                    Description = t.Function.Description,
                    Parameters = t.Function.Parameters
                }
            }).ToList();
        }

        return ollamaRequest;
    }

    private static OllamaMessage MapMessage(ChatMessage message)
    {
        var role = message.Role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => "user"
        };

        var ollamaMessage = new OllamaMessage
        {
            Role = role,
            Content = message.Content,
            ToolCallId = message.ToolCallId
        };

        if (message.ToolCalls?.Count > 0)
        {
            ollamaMessage.ToolCalls = message.ToolCalls.Select(tc => new OllamaToolCall
            {
                Id = tc.Id,
                Type = "function",
                Function = new OllamaFunctionCall
                {
                    Name = tc.Function.Name,
                    Arguments = tc.Function.Arguments
                }
            }).ToList();
        }

        return ollamaMessage;
    }

    private static ChatCompletion MapToCompletion(OllamaResponse response)
    {
        var choice = response.Choices?.FirstOrDefault();

        var toolCalls = choice?.Message?.ToolCalls?.Select(tc => new ToolCall
        {
            Id = tc.Id ?? Guid.NewGuid().ToString(),
            Function = new FunctionCall
            {
                Name = tc.Function?.Name ?? "unknown",
                Arguments = tc.Function?.Arguments ?? "{}"
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

#region Ollama API DTOs

internal sealed class OllamaRequest
{
    public required string Model { get; set; }
    public required List<OllamaMessage> Messages { get; set; }
    public OllamaRequestOptions? Options { get; set; }
    public List<OllamaTool>? Tools { get; set; }
    public bool Stream { get; set; }
}

internal sealed class OllamaRequestOptions
{
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? NumPredict { get; set; }
    public List<string>? Stop { get; set; }
}

internal sealed class OllamaMessage
{
    public required string Role { get; set; }
    public string? Content { get; set; }
    public string? ToolCallId { get; set; }
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

internal sealed class OllamaTool
{
    public required string Type { get; set; }
    public required OllamaFunction Function { get; set; }
}

internal sealed class OllamaFunction
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public JsonDocument? Parameters { get; set; }
}

internal sealed class OllamaToolCall
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public int Index { get; set; }
    public OllamaFunctionCall? Function { get; set; }
}

internal sealed class OllamaFunctionCall
{
    public string? Name { get; set; }
    public string? Arguments { get; set; }
}

internal sealed class OllamaResponse
{
    public string? Model { get; set; }
    public List<OllamaChoice>? Choices { get; set; }
    public OllamaUsage? Usage { get; set; }
}

internal sealed class OllamaChoice
{
    public OllamaMessage? Message { get; set; }
    public OllamaMessageDelta? Delta { get; set; }
    public string? FinishReason { get; set; }
}

internal sealed class OllamaMessageDelta
{
    public string? Content { get; set; }
    public List<OllamaToolCall>? ToolCalls { get; set; }
}

internal sealed class OllamaUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

internal sealed class OllamaStreamChunk
{
    public string? Model { get; set; }
    public List<OllamaChoice>? Choices { get; set; }
    public OllamaUsage? Usage { get; set; }
}

internal sealed class OllamaModelsResponse
{
    public List<OllamaModelInfo>? Models { get; set; }
}

internal sealed class OllamaModelInfo
{
    public string? Name { get; set; }
    public string? ModifiedAt { get; set; }
    public long? Size { get; set; }
}

#endregion

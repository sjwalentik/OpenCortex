using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Orchestration;

/// <summary>
/// Model provider backed by the local Codex CLI running inside the user's isolated workspace runtime.
/// </summary>
internal sealed class CodexCliModelProvider : IModelProvider
{
    private static readonly string[] BuiltInModels =
    [
        "gpt-5.4",
        "gpt-5-codex",
        "codex-mini-latest"
    ];

    private readonly Guid _userId;
    private readonly string _defaultModel;
    private readonly string _sessionJson;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger _logger;

    public CodexCliModelProvider(
        Guid userId,
        string defaultModel,
        string sessionJson,
        IWorkspaceManager workspaceManager,
        ILogger logger)
    {
        _userId = userId;
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "gpt-5.4" : defaultModel.Trim();
        _sessionJson = sessionJson;
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public string ProviderId => "openai-codex";
    public string Name => "OpenAI Codex";
    public string ProviderType => "openai-codex";
    public ProviderCapabilities Capabilities => new()
    {
        SupportsChat = true,
        SupportsCode = true,
        SupportsVision = false,
        SupportsTools = false,
        SupportsStreaming = false,
        MaxContextTokens = 400000,
        MaxOutputTokens = 128000
    };

    public async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model;
        var prompt = BuildPrompt(request);

        await _workspaceManager.EnsureRunningAsync(
            _userId,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WorkspaceRuntimePaths.CodexProviderId] = _sessionJson
            },
            cancellationToken);

        var homePath = await GetRuntimeHomePathAsync(cancellationToken);
        var command = ResolveCommand();
        var commandResult = await _workspaceManager.ExecuteCommandAsync(
            _userId,
            command.FileName,
            workingDirectory: null,
            environmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HOME"] = homePath,
                ["CODEX_HOME"] = GetCodexHomeDirectory(homePath)
            },
            argumentList: BuildArgumentList(command.PrefixArguments, model, prompt),
            cancellationToken: cancellationToken);

        if (!commandResult.Success)
        {
            _logger.LogWarning(
                "Codex exec failed for user {UserId}. ExitCode={ExitCode}",
                _userId,
                commandResult.ExitCode);
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(commandResult.StandardError)
                    ? "Codex execution failed."
                    : commandResult.StandardError.Trim());
        }

        var parsed = ParseResult(commandResult.StandardOutput, model);
        return new ChatCompletion
        {
            Content = parsed.Content,
            Usage = parsed.Usage,
            FinishReason = FinishReason.Stop,
            Model = model
        };
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var completion = await CompleteAsync(request, cancellationToken);

        if (!string.IsNullOrEmpty(completion.Content))
        {
            yield return new StreamChunk
            {
                ContentDelta = completion.Content,
                Model = completion.Model
            };
        }

        yield return new StreamChunk
        {
            IsComplete = true,
            FinalUsage = completion.Usage,
            FinishReason = completion.FinishReason,
            Model = completion.Model
        };
    }

    public async Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await _workspaceManager.EnsureRunningAsync(
                _userId,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [WorkspaceRuntimePaths.CodexProviderId] = _sessionJson
                },
                cancellationToken);

            var homePath = await GetRuntimeHomePathAsync(cancellationToken);
            var command = ResolveCommand();
            var result = await _workspaceManager.ExecuteCommandAsync(
                _userId,
                command.FileName,
                environmentVariables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["HOME"] = homePath,
                    ["CODEX_HOME"] = GetCodexHomeDirectory(homePath)
                },
                argumentList: [.. command.PrefixArguments, "--version"],
                cancellationToken: cancellationToken);

            stopwatch.Stop();
            return result.Success
                ? ProviderHealthResult.Healthy((int)stopwatch.ElapsedMilliseconds)
                : ProviderHealthResult.Unhealthy(result.StandardError);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Codex provider health check failed for user {UserId}", _userId);
            return ProviderHealthResult.Unhealthy(ex.Message);
        }
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> models = BuiltInModels
            .Select(model => new ModelInfo
            {
                Id = model,
                Name = model,
                OwnedBy = "openai-codex"
            })
            .ToList();

        return Task.FromResult(models);
    }

    private async Task<string> GetRuntimeHomePathAsync(CancellationToken cancellationToken)
    {
        var workspacePath = await _workspaceManager.GetWorkspacePathAsync(_userId, cancellationToken);
        return WorkspaceRuntimePaths.GetCodexHomePath(
            _workspaceManager.SupportsContainerIsolation,
            workspacePath).Replace('\\', '/');
    }

    private static string GetCodexHomeDirectory(string homePath) => $"{homePath.TrimEnd('/', '\\')}/.codex";

    private static IReadOnlyList<string> BuildArgumentList(
        IReadOnlyList<string> prefixArguments,
        string model,
        string prompt)
    {
        var arguments = new List<string>(prefixArguments)
        {
            "exec",
            "--json",
            "--skip-git-repo-check",
            "--color",
            "never",
            "--full-auto",
            "-m",
            model,
            prompt
        };

        return arguments;
    }

    private static string BuildPrompt(ChatRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are responding inside OpenCortex.");
        builder.AppendLine("Use the repository workspace when needed.");
        builder.AppendLine("Return a plain-text assistant reply.");
        builder.AppendLine();
        builder.AppendLine("Conversation:");

        foreach (var message in request.Messages)
        {
            var role = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => $"tool:{message.ToolName ?? "unknown"}",
                _ => "user"
            };

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                builder.AppendLine($"[{role}]");
                builder.AppendLine(message.Content.Trim());
                builder.AppendLine();
            }

            if (message.ToolCalls is { Count: > 0 })
            {
                builder.AppendLine($"[{role}-tool-calls]");
                foreach (var toolCall in message.ToolCalls)
                {
                    builder.AppendLine($"{toolCall.Function.Name}: {toolCall.Function.Arguments}");
                }
                builder.AppendLine();
            }
        }

        return builder.ToString().Trim();
    }

    private static ParsedCodexResult ParseResult(string output, string model)
    {
        string? content = null;
        var usage = TokenUsage.Empty;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith('{'))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                if (string.Equals(eventType, "item.completed", StringComparison.Ordinal)
                    && root.TryGetProperty("item", out var itemElement))
                {
                    var itemType = itemElement.TryGetProperty("type", out var itemTypeElement)
                        ? itemTypeElement.GetString()
                        : null;
                    if (string.Equals(itemType, "agent_message", StringComparison.Ordinal)
                        && itemElement.TryGetProperty("text", out var textElement))
                    {
                        content = textElement.GetString();
                    }
                }

                if (string.Equals(eventType, "turn.completed", StringComparison.Ordinal)
                    && root.TryGetProperty("usage", out var usageElement))
                {
                    usage = new TokenUsage
                    {
                        PromptTokens = GetInt32(usageElement, "input_tokens"),
                        CompletionTokens = GetInt32(usageElement, "output_tokens")
                    };
                }
            }
            catch (JsonException)
            {
            }
        }

        return new ParsedCodexResult(
            content ?? $"Codex completed without returning a message for model {model}.",
            usage);
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : 0;
    }

    private static CommandResolution ResolveCommand()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandResolution("codex", []);
        }

        var vendorBinary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "nodejs",
            "node_modules",
            "@openai",
            "codex",
            "node_modules",
            "@openai",
            "codex-win32-x64",
            "vendor",
            "x86_64-pc-windows-msvc",
            "codex",
            "codex.exe");

        if (File.Exists(vendorBinary))
        {
            return new CommandResolution(vendorBinary, []);
        }

        var scriptPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "nodejs",
            "node_modules",
            "@openai",
            "codex",
            "bin",
            "codex.js");

        return File.Exists(scriptPath)
            ? new CommandResolution("node", [scriptPath])
            : new CommandResolution("codex", []);
    }

    private sealed record ParsedCodexResult(string Content, TokenUsage Usage);

    private sealed record CommandResolution(string FileName, IReadOnlyList<string> PrefixArguments);
}


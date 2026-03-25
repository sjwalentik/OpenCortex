using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenCortex.Providers.Abstractions;
using OpenCortex.Tools;

namespace OpenCortex.Orchestration;

/// <summary>
/// Model provider backed by the Claude CLI running inside the user's isolated workspace runtime.
/// Authenticates via ANTHROPIC_API_KEY environment variable.
/// </summary>
internal sealed class ClaudeCliModelProvider : IModelProvider
{
    private static readonly string[] BuiltInModels =
    [
        "claude-opus-4-6",
        "claude-sonnet-4-6",
        "claude-haiku-4-5"
    ];

    private readonly Guid _userId;
    private readonly string _defaultModel;
    private readonly string? _apiKey;            // set when using api_key auth
    private readonly string? _credentialsJson;   // set when using session_json (subscription) auth
    private readonly string? _githubToken;
    private readonly string? _mcpToken;
    private readonly string? _mcpServerUrl;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ILogger _logger;

    /// <param name="apiKey">Anthropic API key — mutually exclusive with credentialsJson.</param>
    /// <param name="credentialsJson">Claude.ai OAuth credentials JSON — mutually exclusive with apiKey.</param>
    /// <param name="mcpToken">Session-scoped personal API token granting mcp:read access to the OpenCortex MCP server.</param>
    /// <param name="mcpServerUrl">Internal URL of the OpenCortex MCP server (e.g. http://opencortex-mcp:8080/mcp).</param>
    public ClaudeCliModelProvider(
        Guid userId,
        string defaultModel,
        string? apiKey,
        string? credentialsJson,
        string? githubToken,
        string? mcpToken,
        string? mcpServerUrl,
        IWorkspaceManager workspaceManager,
        ILogger logger)
    {
        _userId = userId;
        _defaultModel = string.IsNullOrWhiteSpace(defaultModel) ? "claude-sonnet-4-6" : defaultModel.Trim();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        _credentialsJson = string.IsNullOrWhiteSpace(credentialsJson) ? null : credentialsJson.Trim();
        _githubToken = string.IsNullOrWhiteSpace(githubToken) ? null : githubToken.Trim();
        _mcpToken = string.IsNullOrWhiteSpace(mcpToken) ? null : mcpToken.Trim();
        _mcpServerUrl = string.IsNullOrWhiteSpace(mcpServerUrl) ? null : mcpServerUrl.Trim();
        _workspaceManager = workspaceManager;
        _logger = logger;
    }

    public string ProviderId => "claude-cli";
    public string Name => "Claude (CLI)";
    public string ProviderType => "claude-cli";

    public ProviderCapabilities Capabilities => new()
    {
        SupportsChat = true,
        SupportsCode = true,
        SupportsVision = false,
        SupportsTools = false,
        SupportsStreaming = false,
        MaxContextTokens = 200000,
        MaxOutputTokens = 64000
    };

    public async Task<ChatCompletion> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = string.IsNullOrWhiteSpace(request.Model) ? _defaultModel : request.Model;
        var prompt = BuildPrompt(request);

        var credentialsForSync = BuildCredentialsForSync();

        await _workspaceManager.EnsureRunningAsync(_userId, credentialsForSync, cancellationToken);

        // Always set HOME to the isolated directory so claude reads our settings.json
        // regardless of whether session_json or api_key auth is used.
        var claudeHomePath = await GetClaudeHomePathAsync(cancellationToken);

        var command = ResolveCommand();
        var commandResult = await _workspaceManager.ExecuteCommandAsync(
            _userId,
            command.FileName,
            workingDirectory: null,
            environmentVariables: BuildRuntimeEnvironment(claudeHomePath),
            argumentList: BuildArgumentList(command.PrefixArguments, model, _workspaceManager.SupportsContainerIsolation),
            standardInput: prompt,
            cancellationToken: cancellationToken);

        if (!commandResult.Success)
        {
            _logger.LogWarning(
                "Claude CLI exec failed for user {UserId}. ExitCode={ExitCode} Stderr={Stderr} Stdout={Stdout}",
                _userId,
                commandResult.ExitCode,
                commandResult.StandardError,
                commandResult.StandardOutput);
            throw new InvalidOperationException(GetFailureMessage(commandResult));
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
            await _workspaceManager.EnsureRunningAsync(_userId, BuildCredentialsForSync(), cancellationToken);

            var claudeHomePath = await GetClaudeHomePathAsync(cancellationToken);

            var command = ResolveCommand();
            var result = await _workspaceManager.ExecuteCommandAsync(
                _userId,
                command.FileName,
                environmentVariables: BuildRuntimeEnvironment(claudeHomePath),
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
            _logger.LogWarning(ex, "Claude CLI health check failed for user {UserId}", _userId);
            return ProviderHealthResult.Unhealthy(ex.Message);
        }
    }

    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelInfo> models = BuiltInModels
            .Select(model => new ModelInfo { Id = model, Name = model, OwnedBy = "anthropic" })
            .ToList();

        return Task.FromResult(models);
    }

    private async Task<string> GetClaudeHomePathAsync(CancellationToken cancellationToken)
    {
        var workspacePath = await _workspaceManager.GetWorkspacePathAsync(_userId, cancellationToken);
        return WorkspaceRuntimePaths.GetClaudeHomePath(
            _workspaceManager.SupportsContainerIsolation, workspacePath).Replace('\\', '/');
    }

    private Dictionary<string, string>? BuildCredentialsForSync()
    {
        var creds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_credentialsJson is not null)
        {
            creds[WorkspaceRuntimePaths.ClaudeCliProviderId] = _credentialsJson;
        }

        if (_mcpToken is not null)
        {
            creds[WorkspaceRuntimePaths.ClaudeMcpTokenKey] = _mcpToken;
        }

        if (_mcpServerUrl is not null)
        {
            creds[WorkspaceRuntimePaths.ClaudeMcpServerUrlKey] = _mcpServerUrl;
        }

        return creds.Count > 0 ? creds : null;
    }

    private Dictionary<string, string> BuildRuntimeEnvironment(string? claudeHomePath)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0"
        };

        // API key auth: pass key as env var
        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            environment["ANTHROPIC_API_KEY"] = _apiKey;
        }

        // Always point HOME at the isolated dir so claude reads ~/.claude/settings.json and
        // (for session_json auth) ~/.claude/.credentials.json from the isolated home.
        if (!string.IsNullOrWhiteSpace(claudeHomePath))
        {
            environment["HOME"] = claudeHomePath;
        }

        if (!string.IsNullOrWhiteSpace(_githubToken))
        {
            environment["GITHUB_TOKEN"] = _githubToken;
            environment["GH_TOKEN"] = _githubToken;

            var basicAuth = Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{_githubToken}"));
            environment["GIT_CONFIG_COUNT"] = "1";
            environment["GIT_CONFIG_KEY_0"] = "http.https://github.com/.extraheader";
            environment["GIT_CONFIG_VALUE_0"] = $"AUTHORIZATION: basic {basicAuth}";
        }

        return environment;
    }

    private static IReadOnlyList<string> BuildArgumentList(
        IReadOnlyList<string> prefixArguments,
        string model,
        bool runtimeIsExternallySandboxed)
    {
        var arguments = new List<string>(prefixArguments)
        {
            "--output-format",
            "stream-json",
            "--verbose",
            "--model",
            model
        };

        if (runtimeIsExternallySandboxed)
        {
            arguments.Add("--dangerously-skip-permissions");
        }

        arguments.AddRange(["-p", "-"]);

        return arguments;
    }

    private static string BuildPrompt(ChatRequest request)
    {
        var builder = new StringBuilder();

        foreach (var message in request.Messages)
        {
            if (message.Role == ChatRole.System)
            {
                builder.AppendLine(message.Content?.Trim());
                builder.AppendLine();
                continue;
            }

            var role = message.Role switch
            {
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
        }

        return builder.ToString().Trim();
    }

    private static ParsedResult ParseResult(string output, string model)
    {
        string? content = null;
        var usage = TokenUsage.Empty;

        foreach (var rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!rawLine.StartsWith('{'))
                continue;

            try
            {
                using var document = JsonDocument.Parse(rawLine);
                var root = document.RootElement;
                var eventType = root.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString()
                    : null;

                // Extract text from assistant message events
                if (string.Equals(eventType, "assistant", StringComparison.Ordinal)
                    && root.TryGetProperty("message", out var messageElement)
                    && messageElement.TryGetProperty("content", out var contentArray)
                    && contentArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var contentItem in contentArray.EnumerateArray())
                    {
                        if (contentItem.TryGetProperty("type", out var contentType)
                            && string.Equals(contentType.GetString(), "text", StringComparison.Ordinal)
                            && contentItem.TryGetProperty("text", out var textElement))
                        {
                            content = textElement.GetString();
                        }
                    }
                }

                // Extract usage from result event
                if (string.Equals(eventType, "result", StringComparison.Ordinal)
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

        return new ParsedResult(
            content ?? $"Claude CLI completed without returning a message for model {model}.",
            usage);
    }

    private static string GetFailureMessage(CommandResult result)
    {
        // Try to extract a meaningful error from stream-json stdout first
        var streamError = TryExtractStreamJsonError(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(streamError))
            return streamError;

        // stderr may include both claude's output and the kubectl "command terminated" wrapper message
        // Include stdout too in case claude wrote error details there
        var parts = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            parts.Add(result.StandardError.Trim());
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            parts.Add(result.StandardOutput.Trim());

        return parts.Count > 0 ? string.Join(" | ", parts) : "Claude CLI execution failed.";
    }

    private static string? TryExtractStreamJsonError(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("type", out var t) && string.Equals(t.GetString(), "result", StringComparison.Ordinal)
                    && root.TryGetProperty("subtype", out var st) && string.Equals(st.GetString(), "error", StringComparison.Ordinal)
                    && root.TryGetProperty("error", out var err))
                {
                    return err.GetString();
                }
            }
            catch (JsonException) { }
        }

        return null;
    }

    private static int GetInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed) ? parsed : 0;
    }

    private static CommandResolution ResolveCommand()
    {
        // In containers/pods (Linux) claude is on PATH after npm global install
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new CommandResolution("claude", []);
        }

        // On Windows, try the npm global scripts directory first
        var npmPrefix = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm");

        var cmdPath = Path.Combine(npmPrefix, "claude.cmd");
        if (File.Exists(cmdPath))
        {
            return new CommandResolution(cmdPath, []);
        }

        return new CommandResolution("claude", []);
    }

    private sealed record ParsedResult(string Content, TokenUsage Usage);
    private sealed record CommandResolution(string FileName, IReadOnlyList<string> PrefixArguments);
}

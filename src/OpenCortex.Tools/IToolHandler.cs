using System.Text.Json;

namespace OpenCortex.Tools;

/// <summary>
/// Interface for implementing a tool handler.
/// Each tool handler is responsible for executing a specific tool.
/// </summary>
public interface IToolHandler
{
    /// <summary>
    /// The name of the tool this handler executes.
    /// Must match the tool definition name.
    /// </summary>
    string ToolName { get; }

    /// <summary>
    /// Category for grouping tools (e.g., "github", "filesystem", "shell").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Execute the tool with the given arguments.
    /// </summary>
    /// <param name="arguments">JSON arguments from the tool call.</param>
    /// <param name="context">Execution context with user credentials and workspace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON string result to return to the LLM.</returns>
    Task<string> ExecuteAsync(
        JsonElement arguments,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);
}

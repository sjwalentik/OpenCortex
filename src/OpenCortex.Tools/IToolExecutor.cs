using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools;

/// <summary>
/// Orchestrates tool execution by routing tool calls to handlers.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Execute a tool call and return the result.
    /// </summary>
    /// <param name="toolCall">The tool call from the LLM.</param>
    /// <param name="context">Execution context with user credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result with output or error.</returns>
    Task<ToolExecutionResult> ExecuteAsync(
        ToolCall toolCall,
        ToolExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all available tool definitions for a user.
    /// </summary>
    /// <param name="userId">User ID to check configured providers.</param>
    /// <param name="categories">Optional filter by categories.</param>
    /// <returns>List of tool definitions.</returns>
    IReadOnlyList<ToolDefinition> GetAvailableTools(
        Guid userId,
        IEnumerable<string>? categories = null);

    /// <summary>
    /// Get tool definitions by name.
    /// </summary>
    /// <param name="toolNames">Names of tools to get.</param>
    /// <returns>List of matching tool definitions.</returns>
    IReadOnlyList<ToolDefinition> GetToolsByName(IEnumerable<string> toolNames);

    /// <summary>
    /// Check if a tool exists.
    /// </summary>
    bool HasTool(string toolName);
}

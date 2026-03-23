using System.Text.Json;

namespace OpenCortex.Providers.Abstractions;

/// <summary>
/// Definition of a tool available for the model to call.
/// </summary>
public sealed record ToolDefinition
{
    /// <summary>
    /// Type of tool (currently always "function").
    /// </summary>
    public string Type => "function";

    /// <summary>
    /// Function definition.
    /// </summary>
    public required FunctionDefinition Function { get; init; }

    /// <summary>
    /// Create a tool definition from a function.
    /// </summary>
    public static ToolDefinition FromFunction(
        string name,
        string description,
        JsonDocument? parameters = null) =>
        new()
        {
            Function = new FunctionDefinition
            {
                Name = name,
                Description = description,
                Parameters = parameters
            }
        };
}

/// <summary>
/// Definition of a function that can be called by the model.
/// </summary>
public sealed record FunctionDefinition
{
    /// <summary>
    /// Name of the function.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Description of what the function does.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// JSON Schema for the function parameters.
    /// </summary>
    public JsonDocument? Parameters { get; init; }
}

/// <summary>
/// A tool call made by the model.
/// </summary>
public sealed record ToolCall
{
    /// <summary>
    /// Unique identifier for this tool call.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Type of tool (currently always "function").
    /// </summary>
    public string Type => "function";

    /// <summary>
    /// Function call details.
    /// </summary>
    public required FunctionCall Function { get; init; }
}

/// <summary>
/// Details of a function call made by the model.
/// </summary>
public sealed record FunctionCall
{
    /// <summary>
    /// Name of the function to call.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// JSON-encoded arguments for the function.
    /// </summary>
    public required string Arguments { get; init; }
}

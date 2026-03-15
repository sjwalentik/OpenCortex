using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools;

/// <summary>
/// Provider of tool definitions.
/// Implement this interface to register tools from different modules.
/// </summary>
public interface IToolDefinitionProvider
{
    /// <summary>
    /// Get all tool definitions provided by this module.
    /// </summary>
    IReadOnlyList<ToolDefinition> GetToolDefinitions();
}

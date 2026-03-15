using System.Text.Json;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools.FileSystem;

/// <summary>
/// Tool definitions for file system operations.
/// </summary>
public sealed class FileSystemToolDefinitions : IToolDefinitionProvider
{
    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return new[]
        {
            ReadFile,
            WriteFile,
            ListDirectory,
            CreateDirectory,
            DeleteFile
        };
    }

    public static ToolDefinition ReadFile => ToolDefinition.FromFunction(
        name: "read_file",
        description: "Read the contents of a file from the workspace. " +
                     "Returns the file content as text.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the file within the workspace"
                }
            },
            "required": ["path"]
        }
        """)
    );

    public static ToolDefinition WriteFile => ToolDefinition.FromFunction(
        name: "write_file",
        description: "Write content to a file in the workspace. " +
                     "Creates the file if it doesn't exist, or overwrites it if it does. " +
                     "Parent directories are created automatically.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the file within the workspace"
                },
                "content": {
                    "type": "string",
                    "description": "Content to write to the file"
                }
            },
            "required": ["path", "content"]
        }
        """)
    );

    public static ToolDefinition ListDirectory => ToolDefinition.FromFunction(
        name: "list_directory",
        description: "List the contents of a directory in the workspace. " +
                     "Returns a list of files and subdirectories.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the directory (use '.' or '' for workspace root)"
                },
                "recursive": {
                    "type": "boolean",
                    "description": "If true, list contents recursively",
                    "default": false
                }
            },
            "required": []
        }
        """)
    );

    public static ToolDefinition CreateDirectory => ToolDefinition.FromFunction(
        name: "create_directory",
        description: "Create a directory in the workspace. " +
                     "Parent directories are created automatically.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path of the directory to create"
                }
            },
            "required": ["path"]
        }
        """)
    );

    public static ToolDefinition DeleteFile => ToolDefinition.FromFunction(
        name: "delete_file",
        description: "Delete a file or empty directory from the workspace.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative path to the file or directory to delete"
                }
            },
            "required": ["path"]
        }
        """)
    );
}

using System.Text.Json;
using OpenCortex.Providers.Abstractions;

namespace OpenCortex.Tools.GitHub;

/// <summary>
/// Tool definitions for GitHub operations.
/// </summary>
public sealed class GitHubToolDefinitions : IToolDefinitionProvider
{
    public IReadOnlyList<ToolDefinition> GetToolDefinitions()
    {
        return new[]
        {
            GetRepository,
            ListRepositoryFiles,
            GetFileContent,
            CreateOrUpdateFile,
            ListBranches,
            CreateBranch,
            CreatePullRequest,
            GetPullRequest
        };
    }

    public static ToolDefinition GetRepository => ToolDefinition.FromFunction(
        name: "github_get_repository",
        description: "Get information about a GitHub repository including its default branch, " +
                     "visibility, and other metadata.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                }
            },
            "required": ["owner", "repo"]
        }
        """)
    );

    public static ToolDefinition ListRepositoryFiles => ToolDefinition.FromFunction(
        name: "github_list_repository_files",
        description: "List files and directories in a GitHub repository. " +
                     "Returns file names, types (file/dir), sizes, and paths.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "path": {
                    "type": "string",
                    "description": "Path within the repository (empty for root)"
                },
                "ref": {
                    "type": "string",
                    "description": "Branch, tag, or commit SHA (defaults to default branch)"
                }
            },
            "required": ["owner", "repo"]
        }
        """)
    );

    public static ToolDefinition GetFileContent => ToolDefinition.FromFunction(
        name: "github_get_file_content",
        description: "Get the contents of a file from a GitHub repository. " +
                     "Returns the file content as text.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "path": {
                    "type": "string",
                    "description": "Path to the file within the repository"
                },
                "ref": {
                    "type": "string",
                    "description": "Branch, tag, or commit SHA (defaults to default branch)"
                }
            },
            "required": ["owner", "repo", "path"]
        }
        """)
    );

    public static ToolDefinition CreateOrUpdateFile => ToolDefinition.FromFunction(
        name: "github_create_or_update_file",
        description: "Create or update a file in a GitHub repository. " +
                     "This creates a commit with the file changes.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "path": {
                    "type": "string",
                    "description": "Path for the file within the repository"
                },
                "content": {
                    "type": "string",
                    "description": "Content to write to the file"
                },
                "message": {
                    "type": "string",
                    "description": "Commit message for the change"
                },
                "branch": {
                    "type": "string",
                    "description": "Branch to commit to"
                },
                "sha": {
                    "type": "string",
                    "description": "SHA of the file being replaced (required for updates, omit for new files)"
                }
            },
            "required": ["owner", "repo", "path", "content", "message", "branch"]
        }
        """)
    );

    public static ToolDefinition ListBranches => ToolDefinition.FromFunction(
        name: "github_list_branches",
        description: "List all branches in a GitHub repository.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                }
            },
            "required": ["owner", "repo"]
        }
        """)
    );

    public static ToolDefinition CreateBranch => ToolDefinition.FromFunction(
        name: "github_create_branch",
        description: "Create a new branch in a GitHub repository from an existing branch.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "branch_name": {
                    "type": "string",
                    "description": "Name for the new branch"
                },
                "from_branch": {
                    "type": "string",
                    "description": "Source branch to create from (e.g., 'main')"
                }
            },
            "required": ["owner", "repo", "branch_name", "from_branch"]
        }
        """)
    );

    public static ToolDefinition CreatePullRequest => ToolDefinition.FromFunction(
        name: "github_create_pull_request",
        description: "Create a pull request in a GitHub repository. " +
                     "The head branch must have commits not in the base branch.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "title": {
                    "type": "string",
                    "description": "Pull request title"
                },
                "body": {
                    "type": "string",
                    "description": "Pull request description"
                },
                "head": {
                    "type": "string",
                    "description": "Branch containing the changes"
                },
                "base": {
                    "type": "string",
                    "description": "Branch to merge into (e.g., 'main')"
                }
            },
            "required": ["owner", "repo", "title", "body", "head", "base"]
        }
        """)
    );

    public static ToolDefinition GetPullRequest => ToolDefinition.FromFunction(
        name: "github_get_pull_request",
        description: "Get details about a specific pull request.",
        parameters: JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "owner": {
                    "type": "string",
                    "description": "Repository owner (user or organization)"
                },
                "repo": {
                    "type": "string",
                    "description": "Repository name"
                },
                "number": {
                    "type": "integer",
                    "description": "Pull request number"
                }
            },
            "required": ["owner", "repo", "number"]
        }
        """)
    );
}

namespace OpenCortex.Tools.GitHub;

/// <summary>
/// Abstraction for GitHub API operations.
/// </summary>
public interface IGitHubClient
{
    /// <summary>
    /// List files in a repository.
    /// </summary>
    Task<GitHubFileInfo[]> ListRepositoryFilesAsync(
        string token,
        string owner,
        string repo,
        string? path = null,
        string? @ref = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file content from a repository.
    /// </summary>
    Task<GitHubFileContent> GetFileContentAsync(
        string token,
        string owner,
        string repo,
        string path,
        string? @ref = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create or update a file in a repository.
    /// </summary>
    Task<GitHubCommitResult> CreateOrUpdateFileAsync(
        string token,
        string owner,
        string repo,
        string path,
        string content,
        string message,
        string branch,
        string? sha = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// List branches in a repository.
    /// </summary>
    Task<GitHubBranch[]> ListBranchesAsync(
        string token,
        string owner,
        string repo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new branch.
    /// </summary>
    Task<GitHubBranch> CreateBranchAsync(
        string token,
        string owner,
        string repo,
        string branchName,
        string fromBranch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a pull request.
    /// </summary>
    Task<GitHubPullRequest> CreatePullRequestAsync(
        string token,
        string owner,
        string repo,
        string title,
        string body,
        string head,
        string @base,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pull request details.
    /// </summary>
    Task<GitHubPullRequest> GetPullRequestAsync(
        string token,
        string owner,
        string repo,
        int number,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get repository information.
    /// </summary>
    Task<GitHubRepository> GetRepositoryAsync(
        string token,
        string owner,
        string repo,
        CancellationToken cancellationToken = default);
}

public record GitHubFileInfo(
    string Name,
    string Path,
    string Type,
    long Size,
    string Sha,
    string? DownloadUrl);

public record GitHubFileContent(
    string Name,
    string Path,
    string Sha,
    string Content,
    string Encoding);

public record GitHubCommitResult(
    string Sha,
    string Message,
    string HtmlUrl);

public record GitHubBranch(
    string Name,
    string Sha);

public record GitHubPullRequest(
    int Number,
    string Title,
    string Body,
    string State,
    string HtmlUrl,
    string Head,
    string Base,
    string User);

public record GitHubRepository(
    string Name,
    string FullName,
    string Description,
    string DefaultBranch,
    string HtmlUrl,
    bool Private);

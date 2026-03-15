using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenCortex.Tools.GitHub;

/// <summary>
/// GitHub API client using HttpClient.
/// </summary>
public sealed class HttpGitHubClient : IGitHubClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    public HttpGitHubClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.github.com/");
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("OpenCortex", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<GitHubFileInfo[]> ListRepositoryFilesAsync(
        string token,
        string owner,
        string repo,
        string? path = null,
        string? @ref = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}/contents/{path ?? ""}".TrimEnd('/') +
            (@ref != null ? $"?ref={Uri.EscapeDataString(@ref)}" : ""),
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var items = await response.Content.ReadFromJsonAsync<GitHubContentItem[]>(JsonOptions, cancellationToken);
        return items?.Select(i => new GitHubFileInfo(
            i.Name,
            i.Path,
            i.Type,
            i.Size,
            i.Sha,
            i.DownloadUrl
        )).ToArray() ?? [];
    }

    public async Task<GitHubFileContent> GetFileContentAsync(
        string token,
        string owner,
        string repo,
        string path,
        string? @ref = null,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}/contents/{path}" +
            (@ref != null ? $"?ref={Uri.EscapeDataString(@ref)}" : ""),
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var item = await response.Content.ReadFromJsonAsync<GitHubContentItem>(JsonOptions, cancellationToken);
        if (item is null)
            throw new InvalidOperationException("Failed to parse file content response");

        var content = item.Encoding == "base64"
            ? Encoding.UTF8.GetString(Convert.FromBase64String(item.Content ?? ""))
            : item.Content ?? "";

        return new GitHubFileContent(
            item.Name,
            item.Path,
            item.Sha,
            content,
            item.Encoding ?? "none"
        );
    }

    public async Task<GitHubCommitResult> CreateOrUpdateFileAsync(
        string token,
        string owner,
        string repo,
        string path,
        string content,
        string message,
        string branch,
        string? sha = null,
        CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object>
        {
            ["message"] = message,
            ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
            ["branch"] = branch
        };
        if (sha != null)
            payload["sha"] = sha;

        using var request = CreateRequest(HttpMethod.Put,
            $"repos/{owner}/{repo}/contents/{path}",
            token,
            JsonSerializer.Serialize(payload, JsonOptions));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<GitHubCreateFileResponse>(JsonOptions, cancellationToken);
        return new GitHubCommitResult(
            result?.Commit?.Sha ?? "",
            result?.Commit?.Message ?? message,
            result?.Commit?.HtmlUrl ?? ""
        );
    }

    public async Task<GitHubBranch[]> ListBranchesAsync(
        string token,
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}/branches",
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var branches = await response.Content.ReadFromJsonAsync<GitHubBranchItem[]>(JsonOptions, cancellationToken);
        return branches?.Select(b => new GitHubBranch(b.Name, b.Commit?.Sha ?? "")).ToArray() ?? [];
    }

    public async Task<GitHubBranch> CreateBranchAsync(
        string token,
        string owner,
        string repo,
        string branchName,
        string fromBranch,
        CancellationToken cancellationToken = default)
    {
        // First get the SHA of the source branch
        using var getRefRequest = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}/git/ref/heads/{Uri.EscapeDataString(fromBranch)}",
            token);

        using var getRefResponse = await _httpClient.SendAsync(getRefRequest, cancellationToken);
        await EnsureSuccessAsync(getRefResponse, cancellationToken);

        var sourceRef = await getRefResponse.Content.ReadFromJsonAsync<GitHubRef>(JsonOptions, cancellationToken);
        var sha = sourceRef?.Object?.Sha
            ?? throw new InvalidOperationException($"Could not get SHA for branch {fromBranch}");

        // Create the new branch reference
        var payload = new
        {
            @ref = $"refs/heads/{branchName}",
            sha
        };

        using var createRequest = CreateRequest(HttpMethod.Post,
            $"repos/{owner}/{repo}/git/refs",
            token,
            JsonSerializer.Serialize(payload, JsonOptions));

        using var createResponse = await _httpClient.SendAsync(createRequest, cancellationToken);
        await EnsureSuccessAsync(createResponse, cancellationToken);

        return new GitHubBranch(branchName, sha);
    }

    public async Task<GitHubPullRequest> CreatePullRequestAsync(
        string token,
        string owner,
        string repo,
        string title,
        string body,
        string head,
        string @base,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            title,
            body,
            head,
            @base
        };

        using var request = CreateRequest(HttpMethod.Post,
            $"repos/{owner}/{repo}/pulls",
            token,
            JsonSerializer.Serialize(payload, JsonOptions));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var pr = await response.Content.ReadFromJsonAsync<GitHubPullRequestResponse>(JsonOptions, cancellationToken);
        return new GitHubPullRequest(
            pr?.Number ?? 0,
            pr?.Title ?? title,
            pr?.Body ?? body,
            pr?.State ?? "open",
            pr?.HtmlUrl ?? "",
            pr?.Head?.Ref ?? head,
            pr?.Base?.Ref ?? @base,
            pr?.User?.Login ?? ""
        );
    }

    public async Task<GitHubPullRequest> GetPullRequestAsync(
        string token,
        string owner,
        string repo,
        int number,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}/pulls/{number}",
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var pr = await response.Content.ReadFromJsonAsync<GitHubPullRequestResponse>(JsonOptions, cancellationToken);
        return new GitHubPullRequest(
            pr?.Number ?? number,
            pr?.Title ?? "",
            pr?.Body ?? "",
            pr?.State ?? "",
            pr?.HtmlUrl ?? "",
            pr?.Head?.Ref ?? "",
            pr?.Base?.Ref ?? "",
            pr?.User?.Login ?? ""
        );
    }

    public async Task<GitHubRepository> GetRepositoryAsync(
        string token,
        string owner,
        string repo,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"repos/{owner}/{repo}",
            token);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var repository = await response.Content.ReadFromJsonAsync<GitHubRepositoryResponse>(JsonOptions, cancellationToken);
        return new GitHubRepository(
            repository?.Name ?? repo,
            repository?.FullName ?? $"{owner}/{repo}",
            repository?.Description ?? "",
            repository?.DefaultBranch ?? "main",
            repository?.HtmlUrl ?? "",
            repository?.Private ?? false
        );
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        string url,
        string token,
        string? jsonBody = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (jsonBody != null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"GitHub API error: {response.StatusCode} - {error}");
        }
    }

    // Internal DTOs for JSON deserialization
    private record GitHubContentItem(
        string Name,
        string Path,
        string Type,
        long Size,
        string Sha,
        [property: JsonPropertyName("download_url")] string? DownloadUrl,
        string? Content,
        string? Encoding);

    private record GitHubBranchItem(
        string Name,
        GitHubCommitRef? Commit);

    private record GitHubCommitRef(string Sha);

    private record GitHubRef(GitHubRefObject? Object);

    private record GitHubRefObject(string Sha);

    private record GitHubCreateFileResponse(GitHubCommitInfo? Commit);

    private record GitHubCommitInfo(
        string Sha,
        string Message,
        [property: JsonPropertyName("html_url")] string HtmlUrl);

    private record GitHubPullRequestResponse(
        int Number,
        string Title,
        string Body,
        string State,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        GitHubPullRequestRef? Head,
        GitHubPullRequestRef? Base,
        GitHubUser? User);

    private record GitHubPullRequestRef(string Ref);

    private record GitHubUser(string Login);

    private record GitHubRepositoryResponse(
        string Name,
        [property: JsonPropertyName("full_name")] string FullName,
        string? Description,
        [property: JsonPropertyName("default_branch")] string DefaultBranch,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        bool Private);
}

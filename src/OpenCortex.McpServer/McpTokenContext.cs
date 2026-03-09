namespace OpenCortex.McpServer;

public sealed record McpTokenContext(
    string ApiTokenId,
    string UserId,
    string CustomerId,
    IReadOnlyList<string> Scopes,
    string TokenPrefix);

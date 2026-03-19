namespace OpenCortex.Api;

internal sealed record FixedWindowRateLimitDescriptor(
    string PolicyName,
    int PermitLimit,
    TimeSpan Window,
    Func<HttpContext, string> ResolvePartitionKey);

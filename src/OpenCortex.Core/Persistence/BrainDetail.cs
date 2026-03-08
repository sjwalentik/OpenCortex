namespace OpenCortex.Core.Persistence;

public sealed record BrainDetail(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string Status,
    string? Description,
    string? CustomerId,
    IReadOnlyList<SourceRootSummary> SourceRoots);

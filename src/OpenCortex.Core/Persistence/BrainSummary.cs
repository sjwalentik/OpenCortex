namespace OpenCortex.Core.Persistence;

public sealed record BrainSummary(
    string BrainId,
    string Name,
    string Slug,
    string Mode,
    string Status,
    int SourceRootCount);

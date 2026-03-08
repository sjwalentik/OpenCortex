namespace OpenCortex.Core.Persistence;

public sealed record RetrievalResultRecord(
    string DocumentId,
    string BrainId,
    string CanonicalPath,
    string Title,
    string? ChunkId,
    string? Snippet,
    double Score,
    string Reason,
    ScoreBreakdown Breakdown);

/// <summary>
/// Per-signal score contributions that sum to the total Score.
/// Each value is in the range [0, N] where N depends on the signal weight.
/// </summary>
public sealed record ScoreBreakdown(
    double KeywordScore,
    double SemanticScore,
    double GraphScore)
{
    public static readonly ScoreBreakdown Zero = new(0.0, 0.0, 0.0);
}

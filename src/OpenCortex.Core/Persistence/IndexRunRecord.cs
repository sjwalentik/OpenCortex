namespace OpenCortex.Core.Persistence;

public sealed record IndexRunRecord(
    string IndexRunId,
    string BrainId,
    string TriggerType,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    int DocumentsSeen,
    int DocumentsIndexed,
    int DocumentsFailed,
    string? ErrorSummary);

namespace OpenCortex.Core.Persistence;

public sealed record IndexRunErrorRecord(
    string IndexRunErrorId,
    string IndexRunId,
    string? SourceRootId,
    string? DocumentPath,
    string ErrorCode,
    string ErrorMessage,
    DateTimeOffset CreatedAt);

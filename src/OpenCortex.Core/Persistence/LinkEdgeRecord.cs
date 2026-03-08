namespace OpenCortex.Core.Persistence;

public sealed record LinkEdgeRecord(
    string LinkEdgeId,
    string BrainId,
    string FromDocumentId,
    string? ToDocumentId,
    string TargetRef,
    string? LinkText,
    string LinkType);

namespace OpenCortex.Indexer.Indexing;

public sealed record IndexingPlan(
    string BrainId,
    string BrainName,
    int SourceRootCount,
    string Schedule,
    string Mode);

namespace OpenCortex.Core.Persistence;

public sealed record SourceRootSummary(
    string SourceRootId,
    string BrainId,
    string Path,
    string PathType,
    bool IsWritable,
    string[] IncludePatterns,
    string[] ExcludePatterns,
    string WatchMode,
    bool IsActive);

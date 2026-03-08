namespace OpenCortex.Core.Brains;

public sealed class SourceRootDefinition
{
    public string SourceRootId { get; init; } = string.Empty;

    public string Path { get; init; } = string.Empty;

    public string PathType { get; init; } = "local";

    public bool IsWritable { get; init; }

    public string[] IncludePatterns { get; init; } = ["**/*.md"];

    public string[] ExcludePatterns { get; init; } = [];

    public string WatchMode { get; init; } = "scheduled";
}

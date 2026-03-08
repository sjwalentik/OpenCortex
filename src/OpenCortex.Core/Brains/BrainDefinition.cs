namespace OpenCortex.Core.Brains;

public sealed class BrainDefinition
{
    public string BrainId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public BrainMode Mode { get; init; } = BrainMode.Filesystem;

    public string? CustomerId { get; init; }

    public string Status { get; init; } = "active";

    public string[] Tags { get; init; } = [];

    public IReadOnlyList<SourceRootDefinition> SourceRoots { get; init; } = [];
}

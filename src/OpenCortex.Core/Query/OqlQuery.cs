namespace OpenCortex.Core.Query;

public sealed record OqlQuery
{
    public string BrainId { get; init; } = string.Empty;

    public string SearchText { get; init; } = string.Empty;

    public IReadOnlyList<OqlFilter> Filters { get; init; } = [];

    public string RankMode { get; init; } = "hybrid";

    public int Limit { get; init; } = 10;
}

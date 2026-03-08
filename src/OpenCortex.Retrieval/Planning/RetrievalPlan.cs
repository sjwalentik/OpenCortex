namespace OpenCortex.Retrieval.Planning;

public sealed record RetrievalPlan(
    string BrainId,
    string SearchText,
    IReadOnlyList<string> Filters,
    string RankMode,
    int Limit,
    IReadOnlyList<string> Steps);

using OpenCortex.Core.Persistence;
using OpenCortex.Core.Query;
using OpenCortex.Retrieval.Planning;

namespace OpenCortex.Retrieval.Execution;

public sealed class OqlQueryExecutor
{
    private readonly OqlParser _parser = new();
    private readonly OqlRetrievalPlanner _planner = new();
    private readonly IDocumentQueryStore _documentQueryStore;

    public OqlQueryExecutor(IDocumentQueryStore documentQueryStore)
    {
        _documentQueryStore = documentQueryStore;
    }

    public async Task<OqlQueryExecutionResult> ExecuteAsync(string oql, CancellationToken cancellationToken = default)
    {
        var query = _parser.Parse(oql);
        var plan = _planner.BuildPlan(oql);
        var results = await _documentQueryStore.SearchAsync(query, cancellationToken);
        return new OqlQueryExecutionResult(plan, results);
    }
}

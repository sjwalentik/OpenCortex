using OpenCortex.Core.Persistence;
using OpenCortex.Retrieval.Planning;

namespace OpenCortex.Retrieval.Execution;

public sealed record OqlQueryExecutionResult(
    RetrievalPlan Plan,
    IReadOnlyList<RetrievalResultRecord> Results);

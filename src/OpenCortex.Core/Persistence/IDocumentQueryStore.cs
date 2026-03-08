using OpenCortex.Core.Query;

namespace OpenCortex.Core.Persistence;

public interface IDocumentQueryStore
{
    Task<IReadOnlyList<RetrievalResultRecord>> SearchAsync(OqlQuery query, CancellationToken cancellationToken = default);
}

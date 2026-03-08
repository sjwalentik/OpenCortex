using OpenCortex.Core.Brains;
using OpenCortex.Core.Configuration;

namespace OpenCortex.Indexer.Indexing;

public sealed class BrainIndexingPlanner
{
    public IReadOnlyList<IndexingPlan> BuildPlans(OpenCortexOptions options)
    {
        return options.Brains
            .Select(brain => new IndexingPlan(
                brain.BrainId,
                brain.Name,
                brain.SourceRoots.Count,
                options.Indexing.DefaultSchedule,
                brain.Mode.ToString()))
            .ToArray();
    }

    public IndexingPlan BuildPlan(BrainDefinition brain, IndexingOptions indexing)
    {
        return new IndexingPlan(
            brain.BrainId,
            brain.Name,
            brain.SourceRoots.Count,
            indexing.DefaultSchedule,
            brain.Mode.ToString());
    }
}

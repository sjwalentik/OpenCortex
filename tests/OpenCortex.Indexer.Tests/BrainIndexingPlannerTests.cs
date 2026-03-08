using OpenCortex.Core.Brains;
using OpenCortex.Core.Configuration;
using OpenCortex.Indexer.Indexing;

namespace OpenCortex.Indexer.Tests;

public sealed class BrainIndexingPlannerTests
{
    [Fact]
    public void BuildPlans_ReturnsOnePlanPerBrain()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            Brains =
            [
                new BrainDefinition
                {
                    BrainId = "sample-team",
                    Name = "Sample Team",
                    Slug = "sample-team",
                    SourceRoots =
                    [
                        new SourceRootDefinition
                        {
                            SourceRootId = "root-1",
                            Path = "knowledge/canonical",
                        },
                    ],
                },
            ],
        };

        var plans = new BrainIndexingPlanner().BuildPlans(options);

        var plan = Assert.Single(plans);
        Assert.Equal("sample-team", plan.BrainId);
        Assert.Equal(1, plan.SourceRootCount);
    }
}

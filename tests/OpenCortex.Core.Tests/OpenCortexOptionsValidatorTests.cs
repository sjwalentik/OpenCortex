using OpenCortex.Core.Brains;
using OpenCortex.Core.Configuration;

namespace OpenCortex.Core.Tests;

public sealed class OpenCortexOptionsValidatorTests
{
    [Fact]
    public void Validate_RequiresSourceRootsForFilesystemBrains()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            Brains =
            [
                new BrainDefinition
                {
                    BrainId = "team-a",
                    Name = "Team A",
                    Slug = "team-a",
                    Mode = BrainMode.Filesystem,
                },
            ],
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.Contains(errors, error => error.Contains("Filesystem brain 'team-a'", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsManagedContentBrainWithoutSourceRoots()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            Brains =
            [
                new BrainDefinition
                {
                    BrainId = "customer-a",
                    Name = "Customer A",
                    Slug = "customer-a",
                    Mode = BrainMode.ManagedContent,
                },
            ],
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.DoesNotContain(errors, error => error.Contains("source root", StringComparison.OrdinalIgnoreCase));
    }
}

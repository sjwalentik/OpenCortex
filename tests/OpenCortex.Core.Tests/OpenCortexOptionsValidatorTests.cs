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

    [Fact]
    public void Validate_RequiresFirebaseProjectIdWhenHostedAuthEnabled()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            HostedAuth = new() { Enabled = true },
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.Contains(errors, error => error.Contains("OpenCortex:HostedAuth:FirebaseProjectId", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AcceptsHostedAuthWhenFirebaseProjectIdIsPresent()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            HostedAuth = new() { Enabled = true, FirebaseProjectId = "demo-project" },
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.DoesNotContain(errors, error => error.Contains("OpenCortex:HostedAuth", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RequiresFreeBillingPlan()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            Billing = new BillingOptions
            {
                Plans = new Dictionary<string, PlanEntitlements>(StringComparer.OrdinalIgnoreCase),
            },
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.Contains(errors, error => error.Contains("Billing:Plans must include a 'free' plan", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RequiresStripeSettingsWhenStripeEnabled()
    {
        var options = new OpenCortexOptions
        {
            Database = new DatabaseOptions { ConnectionString = "Host=localhost" },
            Billing = new BillingOptions
            {
                Stripe = new StripeBillingOptions { Enabled = true },
            },
        };

        var errors = new OpenCortexOptionsValidator().Validate(options);

        Assert.Contains(errors, error => error.Contains("Billing:Stripe:SecretKey", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Billing:Stripe:WebhookSecret", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Billing:Stripe:AppBaseUrl", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("Billing:Stripe:PriceIds", StringComparison.Ordinal));
    }
}

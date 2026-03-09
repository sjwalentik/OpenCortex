using OpenCortex.Core.Brains;

namespace OpenCortex.Core.Configuration;

public sealed class OpenCortexOptionsValidator
{
    public IReadOnlyList<string> Validate(OpenCortexOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Database.ConnectionString))
        {
            errors.Add("OpenCortex:Database:ConnectionString is required.");
        }

        if (options.Embeddings.Dimensions <= 0)
        {
            errors.Add("OpenCortex:Embeddings:Dimensions must be greater than zero.");
        }

        if (options.HostedAuth.Enabled
            && string.IsNullOrWhiteSpace(options.HostedAuth.FirebaseProjectId))
        {
            errors.Add("OpenCortex:HostedAuth:FirebaseProjectId is required when hosted auth is enabled.");
        }

        if (!options.Billing.Plans.TryGetValue("free", out var freePlan))
        {
            errors.Add("OpenCortex:Billing:Plans must include a 'free' plan.");
        }
        else if (freePlan.MaxDocuments <= 0)
        {
            errors.Add("OpenCortex:Billing:Plans:free:MaxDocuments must be greater than zero.");
        }

        if (options.Billing.Stripe.Enabled)
        {
            if (string.IsNullOrWhiteSpace(options.Billing.Stripe.SecretKey))
            {
                errors.Add("OpenCortex:Billing:Stripe:SecretKey is required when Stripe billing is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Billing.Stripe.WebhookSecret))
            {
                errors.Add("OpenCortex:Billing:Stripe:WebhookSecret is required when Stripe billing is enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Billing.Stripe.AppBaseUrl))
            {
                errors.Add("OpenCortex:Billing:Stripe:AppBaseUrl is required when Stripe billing is enabled.");
            }

            if (!options.Billing.Stripe.PriceIds.ContainsKey("pro"))
            {
                errors.Add("OpenCortex:Billing:Stripe:PriceIds must include a 'pro' entry when Stripe billing is enabled.");
            }
        }

        if (string.Equals(options.Embeddings.Provider, "openai-compatible", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.Embeddings.Endpoint))
        {
            errors.Add("OpenCortex:Embeddings:Endpoint is required for the openai-compatible provider.");
        }

        var brainIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brainSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var brain in options.Brains)
        {
            if (string.IsNullOrWhiteSpace(brain.BrainId))
            {
                errors.Add("Each brain must define BrainId.");
            }
            else if (!brainIds.Add(brain.BrainId))
            {
                errors.Add($"Duplicate brain id '{brain.BrainId}'.");
            }

            if (string.IsNullOrWhiteSpace(brain.Slug))
            {
                errors.Add($"Brain '{brain.BrainId}' must define Slug.");
            }
            else if (!brainSlugs.Add(brain.Slug))
            {
                errors.Add($"Duplicate brain slug '{brain.Slug}'.");
            }

            if (string.IsNullOrWhiteSpace(brain.Name))
            {
                errors.Add($"Brain '{brain.BrainId}' must define Name.");
            }

            if (brain.Mode is BrainMode.Filesystem && brain.SourceRoots.Count == 0)
            {
                errors.Add($"Filesystem brain '{brain.BrainId}' must define at least one source root.");
            }

            foreach (var sourceRoot in brain.SourceRoots)
            {
                if (string.IsNullOrWhiteSpace(sourceRoot.SourceRootId))
                {
                    errors.Add($"Brain '{brain.BrainId}' contains a source root without SourceRootId.");
                }

                if (string.IsNullOrWhiteSpace(sourceRoot.Path))
                {
                    errors.Add($"Brain '{brain.BrainId}' source root '{sourceRoot.SourceRootId}' must define Path.");
                }
            }
        }

        return errors;
    }
}

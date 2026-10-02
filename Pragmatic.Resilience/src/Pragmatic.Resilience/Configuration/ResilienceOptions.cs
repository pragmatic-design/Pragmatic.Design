namespace Pragmatic.Resilience.Configuration;

/// <summary>
/// Root configuration for all resilience policies.
/// Bind from "Resilience" section in appsettings.json.
/// </summary>
public sealed class ResilienceOptions
{
    /// <summary>Named policies mapped by name. Use Add/indexer to populate; never replace the dictionary instance at runtime.</summary>
    public Dictionary<string, ResiliencePolicyOptions> Policies { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default policy applied when no specific policy matches.</summary>
    public ResiliencePolicyOptions? Default { get; set; }
}

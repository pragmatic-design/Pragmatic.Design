namespace Pragmatic.Configuration;

/// <summary>
///     Current environment profile. Wraps IHostEnvironment with Pragmatic conventions.
///     Reusable by all modules (I18N, Resilience, Telemetry, etc.).
/// </summary>
public sealed class EnvironmentProfile
{
    /// <summary>The environment name (e.g. "Development", "Staging", "Production").</summary>
    public required string Name { get; init; }

    /// <summary>Returns <c>true</c> when running in the Development environment.</summary>
    public bool IsDevelopment => string.Equals(Name, "Development", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> when running in the Staging environment.</summary>
    public bool IsStaging => string.Equals(Name, "Staging", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> when running in the Production environment.</summary>
    public bool IsProduction => string.Equals(Name, "Production", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <c>true</c> when running in the Testing environment.</summary>
    public bool IsTesting => string.Equals(Name, "Testing", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    ///     Returns <c>true</c> when the environment name matches (case-insensitive).
    ///     Use for custom environment names beyond Development/Staging/Production/Testing.
    /// </summary>
    /// <param name="environmentName">The environment name to check (e.g., "QA", "UAT", "Preview").</param>
    public bool IsEnvironment(string environmentName)
        => string.Equals(Name, environmentName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Optional sub-environment tag (e.g. "eu-west", "canary", "blue").</summary>
    public string? Tag { get; init; }

    /// <summary>
    ///     Precedence chain for configuration overlay. Last wins.
    ///     Example: ["base", "staging", "staging-eu-west"]
    /// </summary>
    public IReadOnlyList<string> ResolutionChain { get; init; } = [];

    /// <summary>Creates from standard environment name with optional tag.</summary>
    /// <param name="environmentName">The environment name (e.g. "Development", "Staging", "Production").</param>
    /// <param name="tag">Optional sub-environment tag (e.g. "eu-west", "canary").</param>
    /// <returns>A profile whose <see cref="ResolutionChain"/> overlays "base" then the environment then the tag.</returns>
    /// <remarks>
    ///     <para>
    ///         The Production environment is intentionally excluded from the resolution chain: only "base" is
    ///         added (plus any tag overlay). This keeps the production overlay equal to the base configuration so
    ///         that environment-specific overlay files are never required in production.
    ///     </para>
    /// </remarks>
    public static EnvironmentProfile From(string environmentName, string? tag = null)
    {
        var chain = new List<string> { "base" };

        if (!string.Equals(environmentName, "Production", StringComparison.OrdinalIgnoreCase))
            chain.Add(environmentName.ToLowerInvariant());

        if (tag is not null)
            chain.Add($"{environmentName.ToLowerInvariant()}-{tag}");

        return new EnvironmentProfile
        {
            Name = environmentName,
            Tag = tag,
            ResolutionChain = chain
        };
    }
}

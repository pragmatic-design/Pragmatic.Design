namespace Pragmatic.SourceGenerator.Compositions.Models;

/// <summary>
///     Contribution for resilience policy wrapping.
///     Detected from [ResiliencePolicy("name")] attribute.
/// </summary>
internal sealed record ResilienceContribution
{
    /// <summary>Named resilience policy (e.g., "payment-provider").</summary>
    public required string PolicyName { get; init; }
}

namespace Pragmatic.Configuration.Startup;

/// <summary>
///     Accumulates the raw configuration keys that must resolve to a value at startup (declared via
///     <c>AddRequiredConfiguration</c>). Distinct from options <c>ValidateOnStart</c>, which validates a bound
///     POCO — this asserts the effective presence of specific keys the app reads directly.
/// </summary>
internal sealed class RequiredConfigurationKeys
{
    /// <summary>The required keys, accumulated across every <c>AddRequiredConfiguration</c> call.</summary>
    public List<string> Keys { get; } = [];
}

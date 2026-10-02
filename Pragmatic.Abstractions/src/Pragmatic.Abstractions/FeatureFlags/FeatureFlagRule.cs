using System.Globalization;

namespace Pragmatic.FeatureFlags;

/// <summary>
///     A single targeting rule for a feature flag. Prefer the typed factories
///     (<see cref="Percentage"/>, <see cref="Tenant"/>, <see cref="User"/>, <see cref="Plan"/>,
///     <see cref="Property"/>) over hand-setting <see cref="Type"/> — they avoid the magic-string pitfall.
/// </summary>
public sealed record FeatureFlagRule
{
    /// <summary>
    ///     Roll the flag out to a percentage (0-100) of users/tenants via deterministic bucketing: the same
    ///     user always lands in the same bucket for a given flag, so the answer never flaps between requests.
    /// </summary>
    /// <remarks>
    ///     The bounds are absolute and deliberately ignore <paramref name="enabled"/>:
    ///     <list type="bullet">
    ///         <item><description><c>&lt;= 0</c> → <c>false</c> for everyone, overriding the flag's global
    ///         <c>Enabled</c> and any later rule. This makes <c>Percentage(0)</c> usable as a kill switch.</description></item>
    ///         <item><description><c>&gt;= 100</c> → <c>true</c> for everyone.</description></item>
    ///         <item><description>In between, a user inside the bucket yields <paramref name="enabled"/> and a
    ///         user outside it does not match at all, so evaluation moves on to the next rule.</description></item>
    ///     </list>
    ///     So <paramref name="enabled"/> governs the in-bucket answer, not the bounds.
    /// </remarks>
    public static FeatureFlagRule Percentage(int percentage, bool enabled = true) =>
        new() { Type = "percentage", Values = [percentage.ToString(CultureInfo.InvariantCulture)], Enabled = enabled };

    /// <summary>Enable for the given tenant id(s).</summary>
    public static FeatureFlagRule Tenant(params string[] tenantIds) =>
        new() { Type = "tenant", Values = tenantIds };

    /// <summary>Enable for the given user id(s).</summary>
    public static FeatureFlagRule User(params string[] userIds) =>
        new() { Type = "user", Values = userIds };

    /// <summary>Enable for the given plan name(s).</summary>
    public static FeatureFlagRule Plan(params string[] plans) =>
        new() { Type = "plan", Values = plans };

    /// <summary>Enable when the context property <paramref name="key"/> matches one of <paramref name="allowedValues"/>.</summary>
    public static FeatureFlagRule Property(string key, params string[] allowedValues) =>
        new() { Type = "property", Values = [key, .. allowedValues] };

    /// <summary>
    ///     The rule types the evaluation engine understands. Anything else is treated as non-matching, so a
    ///     misspelt type silently does nothing — validate against this set when rules come from an external
    ///     source (configuration, a database, an admin UI).
    /// </summary>
    public static IReadOnlyCollection<string> KnownTypes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "tenant", "user", "plan", "environment", "percentage", "property" };

    /// <summary>Whether <paramref name="type"/> is one of the <see cref="KnownTypes"/> (case-insensitive).</summary>
    public static bool IsKnownType(string? type) =>
        type is not null && KnownTypes.Contains(type);

    /// <summary>
    ///     Turns this rule into a denial: when it matches, the flag evaluates to <c>false</c> instead of
    ///     <c>true</c>. Lets the typed factories express "off for these tenants" —
    ///     <c>FeatureFlagRule.Tenant("acme").Denying()</c> — without hand-writing the rule type.
    /// </summary>
    /// <remarks>
    ///     Rules are evaluated in order and the first match wins, so place a denying rule before the
    ///     broader rule it carves an exception out of.
    /// </remarks>
    public FeatureFlagRule Denying() => this with { Enabled = false };

    /// <summary>
    ///     Rule type. Must be one of <c>"tenant"</c>, <c>"user"</c>, <c>"plan"</c>, <c>"environment"</c>,
    ///     <c>"percentage"</c>, <c>"property"</c>. The comparison is case-insensitive, so <c>"Tenant"</c>
    ///     works too — but a value outside that set is silently ignored by the evaluation engine, which
    ///     treats the rule as non-matching. Validate this field when deserializing from external sources,
    ///     and prefer the typed factories above, which cannot be misspelled.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    ///     Values to match (e.g., tenant IDs, user IDs, plan names).
    ///     For percentage rules, contains the rollout percentage as a string.
    /// </summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>Whether the flag should be enabled when this rule matches.</summary>
    public bool Enabled { get; init; } = true;
}

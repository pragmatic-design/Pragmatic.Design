using System.Security.Cryptography;
using System.Text;

namespace Pragmatic.FeatureFlags.Evaluation;

/// <summary>
///     Evaluates feature flag rules against a given context.
///     Rules are evaluated in order — first match wins.
///     If no rule matches, the flag's global Enabled state is used.
/// </summary>
internal static class FeatureFlagEvaluator
{
    /// <summary>
    ///     Evaluates whether a flag is enabled for the given context.
    /// </summary>
    public static bool Evaluate(FeatureFlagDefinition definition, FeatureFlagContext context)
    {
        // If no rules, use global state
        if (definition.Rules.Count == 0)
            return definition.Enabled;

        // Evaluate rules in order — first match wins
        foreach (var rule in definition.Rules)
        {
            var matches = EvaluateRule(rule, context, definition.Name);
            if (matches.HasValue)
                return matches.Value;
        }

        // No rule matched — fall back to global state
        return definition.Enabled;
    }

    /// <summary>
    ///     Returns true/false if the rule matches, null if the rule doesn't apply.
    /// </summary>
    private static bool? EvaluateRule(FeatureFlagRule rule, FeatureFlagContext context, string flagName)
    {
        var type = rule.Type;
        if (string.Equals(type, "tenant", StringComparison.OrdinalIgnoreCase))     return EvaluateListRule(rule, context.TenantId);
        if (string.Equals(type, "user", StringComparison.OrdinalIgnoreCase))       return EvaluateListRule(rule, context.UserId);
        if (string.Equals(type, "plan", StringComparison.OrdinalIgnoreCase))       return EvaluateListRule(rule, context.Plan);
        if (string.Equals(type, "environment", StringComparison.OrdinalIgnoreCase)) return EvaluateListRule(rule, context.Environment);
        if (string.Equals(type, "percentage", StringComparison.OrdinalIgnoreCase)) return EvaluatePercentageRule(rule, context, flagName);
        if (string.Equals(type, "property", StringComparison.OrdinalIgnoreCase))   return EvaluatePropertyRule(rule, context);
        return null; // Unknown rule type — skip
    }

    private static bool? EvaluateListRule(FeatureFlagRule rule, string? contextValue)
    {
        if (contextValue is null)
            return null;

        var values = rule.Values;
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], contextValue, StringComparison.OrdinalIgnoreCase))
                return rule.Enabled;
        }

        return null;
    }

    private static bool? EvaluatePercentageRule(FeatureFlagRule rule, FeatureFlagContext context, string flagName)
    {
        if (rule.Values.Count == 0 || !int.TryParse(rule.Values[0], out var percentage))
            return null;

        // NOTE (design smell, intentional & covered by tests): the borders return hardcoded false/true
        // ("0% → nobody", "100% → everybody"), whereas the in-bucket path below returns rule.Enabled.
        // So rule.Enabled is the toggle for the in-range case but is ignored at the borders. This is the
        // documented behavior (a percentage rule means "X% of users are IN the rollout"); kept as-is to
        // avoid changing flag evaluation. If unified later, decide once whether rule.Enabled gates the
        // whole rule or only the in-bucket result, and update both the borders and the tests together.
        if (percentage <= 0) return false;
        if (percentage >= 100) return true;

        // Deterministic hash based on flag name + user/tenant for consistent rollout.
        // Mask the sign bit instead of Math.Abs — Math.Abs(int.MinValue) throws OverflowException.
        var seed = context.UserId ?? context.TenantId ?? "anonymous";
        var hash = ComputeStableHash($"{flagName}:{seed}");
        var bucket = (hash & int.MaxValue) % 100;

        return bucket < percentage ? rule.Enabled : null;
    }

    private static bool? EvaluatePropertyRule(FeatureFlagRule rule, FeatureFlagContext context)
    {
        // Property rules: Values[0] = property key, Values[1..] = allowed values
        if (rule.Values.Count < 2)
            return null;

        var propertyKey = rule.Values[0];
        if (!TryGetProperty(context.Properties, propertyKey, out var propertyValue))
            return null;

        var values = rule.Values;
        for (var i = 1; i < values.Count; i++)
        {
            if (string.Equals(values[i], propertyValue, StringComparison.OrdinalIgnoreCase))
                return rule.Enabled;
        }

        return null;
    }

    /// <summary>
    ///     Looks a property up case-insensitively. The dictionary's own comparer is honored first (the fast
    ///     path); the scan only runs when that misses. Without it, matching would be case-sensitive on the
    ///     key and case-insensitive on the value — the caller supplies the dictionary, and a plain
    ///     <c>Dictionary&lt;string, string&gt;</c> defaults to an ordinal comparer.
    /// </summary>
    private static bool TryGetProperty(
        IReadOnlyDictionary<string, string> properties, string key, out string? value)
    {
        if (properties.TryGetValue(key, out value))
            return true;

        foreach (var pair in properties)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    ///     Deterministic hash for percentage rollout — same input always gets same bucket.
    /// </summary>
    private static int ComputeStableHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return BitConverter.ToInt32(bytes, 0);
    }
}

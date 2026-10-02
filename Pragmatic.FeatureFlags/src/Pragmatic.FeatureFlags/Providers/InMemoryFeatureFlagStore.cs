using System.Collections.Concurrent;
using Pragmatic.FeatureFlags.Evaluation;

namespace Pragmatic.FeatureFlags.Providers;

/// <summary>
///     In-memory feature flag store for development and testing.
///     Supports programmatic flag definition and change notification.
/// </summary>
public sealed class InMemoryFeatureFlagStore : IFeatureFlagStore
{
    private readonly ConcurrentDictionary<string, FeatureFlagDefinition> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly FeatureFlagChangeBroadcaster _changes = new();

    /// <summary>Defines or updates a feature flag.</summary>
    /// <exception cref="ArgumentException">
    ///     A rule carries a type the evaluation engine does not know. Such a rule never matches, so the flag
    ///     would silently behave as if it were not there — this is a programming error worth failing on rather
    ///     than shipping. Rules loaded from external configuration are not validated here (see
    ///     <c>ConfigurationFeatureFlagStore</c>): a typo in <c>appsettings.json</c> must not take the app down.
    /// </exception>
    public void Define(FeatureFlagDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        foreach (var rule in definition.Rules)
        {
            if (!FeatureFlagRule.IsKnownType(rule.Type))
                throw new ArgumentException(
                    $"Feature flag '{definition.Name}' has a rule of unknown type '{rule.Type}', which would " +
                    $"never match. Use one of: {string.Join(", ", FeatureFlagRule.KnownTypes)} — or the typed " +
                    $"factories on {nameof(FeatureFlagRule)}, which cannot be misspelt.",
                    nameof(definition));
        }

        var existed = _flags.TryGetValue(definition.Name, out var previous);
        _flags[definition.Name] = definition;

        // Emit a change whenever any property of the definition changes (not only Enabled),
        // so watchers can react to rollout percentage or rules updates too.
        if (existed && HasChanged(previous!, definition))
        {
            _changes.Publish(new FeatureFlagChange(
                definition.Name,
                previous!.Enabled,
                definition.Enabled,
                DateTimeOffset.UtcNow));
        }
    }

    private static bool HasChanged(FeatureFlagDefinition previous, FeatureFlagDefinition next)
    {
        if (previous.Enabled != next.Enabled) return true;
        if (previous.Description != next.Description) return true;
        if (previous.Rules.Count != next.Rules.Count) return true;
        for (var i = 0; i < previous.Rules.Count; i++)
        {
            var pr = previous.Rules[i];
            var nr = next.Rules[i];
            if (pr.Type != nr.Type || pr.Enabled != nr.Enabled)
                return true;
            // Compare the values element-wise, in order: a same-count edit such as a rollout
            // change from ["10"] to ["20"] or a tenant swap ["tenant-a"] -> ["tenant-b"] must
            // be detected, otherwise watchers never observe the change.
            if (!pr.Values.SequenceEqual(nr.Values, StringComparer.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Removes a flag definition.</summary>
    public bool Remove(string flagName) => _flags.TryRemove(flagName, out _);

    public Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
        => IsEnabledAsync(flagName, FeatureFlagContext.Empty, ct);

    public Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default)
    {
        if (!_flags.TryGetValue(flagName, out var definition))
            return Task.FromResult(false); // Unknown flag = disabled

        return Task.FromResult(FeatureFlagEvaluator.Evaluate(definition, context));
    }

    public Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default)
    {
        _flags.TryGetValue(flagName, out var definition);
        return Task.FromResult(definition);
    }

    public Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        var snapshot = new List<FeatureFlagDefinition>(_flags.Values);
        snapshot.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));
        return Task.FromResult<IReadOnlyList<FeatureFlagDefinition>>(snapshot);
    }

    /// <summary>
    ///     Streams flag changes. Every concurrent watcher receives every change — they are broadcast,
    ///     not shared out between watchers.
    /// </summary>
    public IAsyncEnumerable<FeatureFlagChange> WatchAsync(CancellationToken ct = default)
        => _changes.WatchAsync(ct);
}

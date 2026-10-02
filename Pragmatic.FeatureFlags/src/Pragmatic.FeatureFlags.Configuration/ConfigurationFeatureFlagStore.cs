using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Pragmatic.FeatureFlags.Evaluation;
using Pragmatic.FeatureFlags.Providers;

namespace Pragmatic.FeatureFlags.Configuration;

/// <summary>
///     <see cref="IFeatureFlagStore" /> that reads definitions from an
///     <see cref="IConfiguration" /> section (default name: <c>"FeatureFlags"</c>).
///     <para>
///         Honors <see cref="IChangeToken" /> reloads from the underlying configuration
///         providers, so flag changes pushed by <c>appsettings.json</c> file watchers,
///         environment variables, Azure App Configuration or Consul providers propagate at
///         runtime. Each transition of <see cref="FeatureFlagDefinition.Enabled" /> is
///         emitted as a <see cref="FeatureFlagChange" /> on <see cref="WatchAsync" />.
///     </para>
/// </summary>
public sealed class ConfigurationFeatureFlagStore : IFeatureFlagStore, IDisposable
{
    /// <summary>Default name of the configuration section flags are read from.</summary>
    public const string DefaultSectionName = "FeatureFlags";

    private readonly IConfiguration _section;
    private readonly FeatureFlagChangeBroadcaster _changes = new();
    private IDisposable? _reloadRegistration;
    private IReadOnlyDictionary<string, FeatureFlagDefinition> _cache;

    public ConfigurationFeatureFlagStore(IConfiguration configuration, string sectionName = DefaultSectionName)
    {
        _section = configuration.GetSection(sectionName);
        _cache = LoadFlags(_section);
        RegisterReload();
    }

    public Task<bool> IsEnabledAsync(string flagName, CancellationToken ct = default)
        => IsEnabledAsync(flagName, FeatureFlagContext.Empty, ct);

    public Task<bool> IsEnabledAsync(string flagName, FeatureFlagContext context, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(flagName, out var definition))
            return Task.FromResult(false);
        return Task.FromResult(FeatureFlagEvaluator.Evaluate(definition, context));
    }

    public Task<FeatureFlagDefinition?> GetDefinitionAsync(string flagName, CancellationToken ct = default)
    {
        _cache.TryGetValue(flagName, out var definition);
        return Task.FromResult(definition);
    }

    public Task<IReadOnlyList<FeatureFlagDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<FeatureFlagDefinition> result = _cache.Values.OrderBy(f => f.Name).ToList();
        return Task.FromResult(result);
    }

    /// <summary>
    ///     Streams flag changes. Every concurrent watcher receives every change — they are broadcast,
    ///     not shared out between watchers.
    /// </summary>
    public IAsyncEnumerable<FeatureFlagChange> WatchAsync(CancellationToken ct = default)
        => _changes.WatchAsync(ct);

    public void Dispose()
    {
        _reloadRegistration?.Dispose();
        _changes.Complete();
    }

    private void RegisterReload()
    {
        _reloadRegistration?.Dispose();
        _reloadRegistration = ChangeToken.OnChange(
            () => _section.GetReloadToken(),
            ReloadFlags);
    }

    private void ReloadFlags()
    {
        var next = LoadFlags(_section);
        var previous = Interlocked.Exchange(ref _cache, next);
        EmitChanges(previous, next);
    }

    private void EmitChanges(
        IReadOnlyDictionary<string, FeatureFlagDefinition> previous,
        IReadOnlyDictionary<string, FeatureFlagDefinition> next)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (name, definition) in next)
        {
            var wasEnabled = previous.TryGetValue(name, out var prev) && prev.Enabled;
            if (wasEnabled != definition.Enabled)
                _changes.Publish(new FeatureFlagChange(name, wasEnabled, definition.Enabled, now));
        }
        foreach (var (name, prev) in previous)
        {
            if (!next.ContainsKey(name) && prev.Enabled)
                _changes.Publish(new FeatureFlagChange(name, true, false, now));
        }
    }

    private static IReadOnlyDictionary<string, FeatureFlagDefinition> LoadFlags(IConfiguration section)
    {
        var dict = new Dictionary<string, FeatureFlagDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var flag in section.GetChildren())
        {
            // A flag may be declared as a plain boolean ("FeatureName": true) for the
            // common no-rules case, or as a sub-section ("FeatureName": { Enabled, Rules }).
            if (bool.TryParse(flag.Value, out var simpleEnabled))
            {
                dict[flag.Key] = new FeatureFlagDefinition { Name = flag.Key, Enabled = simpleEnabled };
                continue;
            }

            dict[flag.Key] = new FeatureFlagDefinition
            {
                Name = flag.Key,
                Enabled = flag.GetValue<bool>("Enabled"),
                Description = flag["Description"],
                Rules = LoadRules(flag.GetSection("Rules"))
            };
        }

        return dict;
    }

    private static IReadOnlyList<FeatureFlagRule> LoadRules(IConfigurationSection rulesSection)
    {
        if (!rulesSection.Exists())
            return [];

        var rules = new List<FeatureFlagRule>();
        foreach (var rule in rulesSection.GetChildren())
        {
            var values = rule.GetSection("Values").Get<string[]>() ?? [];
            rules.Add(new FeatureFlagRule
            {
                Type = rule["Type"] ?? string.Empty,
                Values = values,
                Enabled = rule.GetValue<bool?>("Enabled") ?? true
            });
        }
        return rules;
    }
}

namespace Pragmatic.Configuration.Database;

/// <summary>Which kind of change a <see cref="ConfigurationChangeSignal" /> reports.</summary>
public enum ConfigurationChangeKind
{
    /// <summary>A row was inserted or updated — its current value can be read.</summary>
    Upsert,

    /// <summary>A row was deleted — no value remains (polling alone cannot observe this).</summary>
    Delete
}

/// <summary>
///     A low-latency push signal that a configuration row changed, delivered by a provider-native mechanism
///     (e.g. PostgreSQL <c>LISTEN/NOTIFY</c>). Unlike polling, it also reports deletes.
/// </summary>
/// <param name="Key">The configuration key that changed.</param>
/// <param name="TenantId">The tenant the change applies to, or <c>null</c> for base configuration.</param>
/// <param name="Environment">The environment of the changed row, or <c>null</c>.</param>
/// <param name="Kind">Whether the row was upserted or deleted.</param>
public sealed record ConfigurationChangeSignal(
    string Key,
    string? TenantId,
    string? Environment,
    ConfigurationChangeKind Kind);

/// <summary>
///     A provider-native push source for configuration changes. When one is registered, the database store
///     watches changes with low latency (push) while still polling periodically as a reconcile safety net.
///     Absent a notifier, the store polls only.
/// </summary>
public interface IConfigurationChangeNotifier
{
    /// <summary>
    ///     Streams change signals until <paramref name="ct" /> is cancelled. Implementations should recover
    ///     from transient disconnects internally; the store's periodic reconcile covers any gap.
    /// </summary>
    IAsyncEnumerable<ConfigurationChangeSignal> ListenAsync(CancellationToken ct);
}

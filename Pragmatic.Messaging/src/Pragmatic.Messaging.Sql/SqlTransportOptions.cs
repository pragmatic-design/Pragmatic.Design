using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     Configuration for the SQL transport (durable queues on PostgreSQL or SQL Server tables).
///     Defaults are loss-safe: dead-lettering on, schema auto-created, polling always active.
/// </summary>
public sealed class SqlTransportOptions
{
    /// <summary>
    ///     Configures the transport's DbContext with the EF provider and connection —
    ///     e.g. <c>o => o.UseNpgsql(cs)</c> or <c>o => o.UseSqlServer(cs)</c>. Required.
    /// </summary>
    public required Action<DbContextOptionsBuilder> ConfigureDbContext { get; set; }

    /// <summary>
    ///     Creates the transport tables at startup with idempotent <c>IF NOT EXISTS</c> DDL
    ///     (broker-style provisioning). Concurrent instances racing the same DDL is benign — the
    ///     create is a no-op once the table exists. Set false when the tables are co-located in the
    ///     app's DbContext (<c>SqlTransportDbContext.ApplyTransportConfigurations</c>) and migrated
    ///     with Pragmatic.Migrations. Default: true.
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    ///     How long a publish, send or subscribe waits for a connect still in progress before it fails.
    ///     The consumer service connects in the background (the connect creates the schema), so the
    ///     application starts with its database down; an operation issued meanwhile waits for the connect
    ///     rather than failing with "not connected". Default: 30 seconds.
    /// </summary>
    public TimeSpan ConnectWaitTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Base poll interval per queue. Default: 1 second.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Max poll interval after consecutive empty polls (adaptive backoff). Default: 10 seconds.</summary>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Messages claimed per poll per queue. Default: 10.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    ///     How long a claimed message stays locked before it becomes reclaimable (crash
    ///     recovery). Handlers slower than the lease may cause duplicate delivery
    ///     (at-least-once). Default: 5 minutes.
    /// </summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Delivery attempts before a message moves to the dead-letter table. Default: 10.</summary>
    public int MaxDeliveryCount { get; set; } = 10;

    /// <summary>
    ///     Uses PostgreSQL LISTEN/NOTIFY to wake consumers on publish (low latency); ignored on
    ///     other providers. Polling remains active as the safety net either way. Default: true.
    /// </summary>
    public bool UseNotifications { get; set; } = true;

    /// <summary>The pg_notify channel name. Default: "pragmatic_messaging".</summary>
    public string NotificationChannel { get; set; } = "pragmatic_messaging";

    /// <summary>How long the publish-side subscription registry cache lives. Default: 30 seconds.</summary>
    public TimeSpan SubscriptionCacheTtl { get; set; } = TimeSpan.FromSeconds(30);
}

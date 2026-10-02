using System.Data.Common;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database;

/// <summary>
///     Options for the database configuration backend.
/// </summary>
public sealed class DatabaseConfigurationOptions
{
    /// <summary>
    ///     Connection string for the configuration database, used with <see cref="ProviderFactory" />.
    ///     Provide via <c>AddDatabaseConfigurationStore(o => o.ConnectionString = "...")</c> or bind from IConfiguration.
    /// </summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    ///     The ADO.NET provider that opens <see cref="ConnectionString" /> — <c>NpgsqlFactory.Instance</c>,
    ///     <c>SqlClientFactory.Instance</c>, <c>SqliteFactory.Instance</c>. With both set, the store needs no
    ///     <see cref="IDbConnectionFactory" />; one the application registers is used instead.
    /// </summary>
    public DbProviderFactory? ProviderFactory { get; set; }

    /// <summary>Database provider to use.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSql;

    /// <summary>Encryption key for secrets (32 bytes, base64-encoded). Required for secret storage.</summary>
    public string? EncryptionKey { get; set; }

    /// <summary>
    ///     Previous encryption keys (32 bytes, base64-encoded) kept only to decrypt secrets written before a
    ///     key rotation. To rotate: set the new key as <see cref="EncryptionKey" /> and move the old key here.
    ///     New writes always use the current key; a re-encrypt pass rewrites old values so these can later be
    ///     dropped.
    /// </summary>
    public IList<string> PreviousEncryptionKeys { get; } = [];

    /// <summary>Whether to auto-create schema on startup.</summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>Environment name for environment-scoped queries. Null = all environments.</summary>
    public string? Environment { get; set; }

    /// <summary>Identity of the current user for audit logging. Null = anonymous.</summary>
    public string? AuditUser { get; set; }

    /// <summary>Polling interval for change detection. Default: 30 seconds.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether to enable polling-based change detection.</summary>
    public bool EnableChangePolling { get; set; } = true;
}

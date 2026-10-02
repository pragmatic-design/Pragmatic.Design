using System.Globalization;

namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     SQLite dialect for development and testing.
///     Uses COALESCE with empty string sentinel for nullable unique columns
///     since SQLite treats NULL != NULL in unique constraints.
/// </summary>
internal sealed class SqliteDialect : ISqlDialect
{
    /// <inheritdoc />
    /// <remarks>Uses backslash as ESCAPE character. Escapes \, %, and _ metacharacters.</remarks>
    public string EscapeLikePattern(string prefix)
        => prefix
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>
    ///     SQLite stores <c>updated_at</c> as text via <c>strftime('%Y-%m-%dT%H:%M:%fZ','now')</c>
    ///     (millisecond precision, UTC). The <c>updated_at &gt; @since</c> comparison is a textual
    ///     comparison, so the cursor must use the identical format to stay precise.
    /// </summary>
    public object FormatChangeCursor(DateTimeOffset cursor)
        => cursor.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    public string GetValue => """
        SELECT value FROM pragmatic_config
        WHERE key = @key
          AND COALESCE(tenant_id, '') = COALESCE(@tenantId, '')
          AND COALESCE(environment, '') = COALESCE(@environment, '')
        """;

    public string SetValue => """
        INSERT INTO pragmatic_config (key, value, tenant_id, environment, version, updated_by, updated_at)
        VALUES (@key, @value, @tenantId, @environment, 1, @updatedBy, strftime('%Y-%m-%dT%H:%M:%fZ','now'))
        ON CONFLICT (key, COALESCE(tenant_id, ''), COALESCE(environment, ''))
        DO UPDATE SET value = @value, version = version + 1,
                      updated_at = strftime('%Y-%m-%dT%H:%M:%fZ','now'), updated_by = @updatedBy
        """;

    public string DeleteValue => """
        DELETE FROM pragmatic_config
        WHERE key = @key
          AND COALESCE(tenant_id, '') = COALESCE(@tenantId, '')
          AND COALESCE(environment, '') = COALESCE(@environment, '')
        """;

    public string GetSection => """
        SELECT key, value FROM pragmatic_config
        WHERE key LIKE @escapedPrefix || '%' ESCAPE '\'
          AND COALESCE(tenant_id, '') = COALESCE(@tenantId, '')
          AND COALESCE(environment, '') = COALESCE(@environment, '')
        ORDER BY key
        """;

    public string GetSecret => """
        SELECT encrypted_value FROM pragmatic_secrets
        WHERE key = @key
          AND COALESCE(tenant_id, '') = COALESCE(@tenantId, '')
        """;

    public string SetSecret => """
        INSERT INTO pragmatic_secrets (key, encrypted_value, tenant_id, updated_by, updated_at)
        VALUES (@key, @encryptedValue, @tenantId, @updatedBy, datetime('now'))
        ON CONFLICT (key, COALESCE(tenant_id, ''))
        DO UPDATE SET encrypted_value = @encryptedValue,
                      updated_at = datetime('now'), updated_by = @updatedBy
        """;

    public string DeleteSecret => """
        DELETE FROM pragmatic_secrets
        WHERE key = @key
          AND COALESCE(tenant_id, '') = COALESCE(@tenantId, '')
        """;

    public string GetAllSecrets => "SELECT key, encrypted_value, tenant_id FROM pragmatic_secrets";

    public string GetChangesSince => """
        SELECT key, value, tenant_id, updated_at FROM pragmatic_config
        WHERE updated_at > @since
          AND COALESCE(environment, '') = COALESCE(@environment, '')
        ORDER BY updated_at
        """;

    public string CreateSchema => """
        CREATE TABLE IF NOT EXISTS pragmatic_config (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            key             TEXT    NOT NULL,
            value           TEXT    NOT NULL,
            tenant_id       TEXT,
            environment     TEXT,
            version         INTEGER NOT NULL DEFAULT 1,
            created_at      TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
            updated_at      TEXT    NOT NULL DEFAULT (strftime('%Y-%m-%dT%H:%M:%fZ','now')),
            updated_by      TEXT
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_config_key
        ON pragmatic_config (key, COALESCE(tenant_id, ''), COALESCE(environment, ''));

        CREATE TABLE IF NOT EXISTS pragmatic_secrets (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            key             TEXT    NOT NULL,
            encrypted_value BLOB    NOT NULL,
            tenant_id       TEXT,
            created_at      TEXT    NOT NULL DEFAULT (datetime('now')),
            updated_at      TEXT    NOT NULL DEFAULT (datetime('now')),
            updated_by      TEXT
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_secret_key
        ON pragmatic_secrets (key, COALESCE(tenant_id, ''));

        CREATE INDEX IF NOT EXISTS ix_config_tenant ON pragmatic_config (key, tenant_id);
        CREATE INDEX IF NOT EXISTS ix_config_env ON pragmatic_config (key, environment);
        """;
}

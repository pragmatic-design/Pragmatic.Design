namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     PostgreSQL dialect with JSONB, ON CONFLICT, and TIMESTAMPTZ support.
/// </summary>
internal sealed class PostgresDialect : ISqlDialect
{
    /// <inheritdoc />
    /// <remarks>Uses backslash as ESCAPE character. Escapes \, %, and _ metacharacters.</remarks>
    public string EscapeLikePattern(string prefix)
        => prefix
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>TIMESTAMPTZ has sub-second precision; the native DateTimeOffset cursor compares precisely.</summary>
    public object FormatChangeCursor(DateTimeOffset cursor) => cursor;

    public string GetValue => """
        SELECT value FROM pragmatic_config
        WHERE key = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        """;

    public string SetValue => """
        INSERT INTO pragmatic_config (key, value, tenant_id, environment, version, updated_by)
        VALUES (@key, @value, @tenantId, @environment, 1, @updatedBy)
        ON CONFLICT (key, COALESCE(tenant_id, ''), COALESCE(environment, ''))
        DO UPDATE SET value = @value, version = pragmatic_config.version + 1,
                      updated_at = now(), updated_by = @updatedBy
        """;

    public string DeleteValue => """
        DELETE FROM pragmatic_config
        WHERE key = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        """;

    public string GetSection => """
        SELECT key, value FROM pragmatic_config
        WHERE key LIKE @escapedPrefix || '%' ESCAPE '\'
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        ORDER BY key
        """;

    public string GetSecret => """
        SELECT encrypted_value FROM pragmatic_secrets
        WHERE key = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
        """;

    public string SetSecret => """
        INSERT INTO pragmatic_secrets (key, encrypted_value, tenant_id, updated_by)
        VALUES (@key, @encryptedValue, @tenantId, @updatedBy)
        ON CONFLICT (key, COALESCE(tenant_id, ''))
        DO UPDATE SET encrypted_value = @encryptedValue,
                      updated_at = now(), updated_by = @updatedBy
        """;

    public string DeleteSecret => """
        DELETE FROM pragmatic_secrets
        WHERE key = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
        """;

    public string GetAllSecrets => "SELECT key, encrypted_value, tenant_id FROM pragmatic_secrets";

    public string GetChangesSince => """
        SELECT key, value, tenant_id, updated_at FROM pragmatic_config
        WHERE updated_at > @since
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        ORDER BY updated_at
        """;

    public string CreateSchema => """
        CREATE TABLE IF NOT EXISTS pragmatic_config (
            id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            key             VARCHAR(500)    NOT NULL,
            value           TEXT            NOT NULL,
            tenant_id       VARCHAR(100),
            environment     VARCHAR(100),
            version         INT             NOT NULL DEFAULT 1,
            created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
            updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
            updated_by      VARCHAR(200)
        );

        -- COALESCE-based unique index: PostgreSQL treats NULLs as distinct in a plain UNIQUE
        -- constraint, so base rows (tenant_id/environment NULL) would never conflict and every
        -- upsert would INSERT a duplicate. Normalizing NULL → '' makes ON CONFLICT fire.
        CREATE UNIQUE INDEX IF NOT EXISTS uq_config_key
        ON pragmatic_config (key, COALESCE(tenant_id, ''), COALESCE(environment, ''));

        CREATE TABLE IF NOT EXISTS pragmatic_secrets (
            id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            key             VARCHAR(500)    NOT NULL,
            encrypted_value BYTEA           NOT NULL,
            tenant_id       VARCHAR(100),
            created_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
            updated_at      TIMESTAMPTZ     NOT NULL DEFAULT now(),
            updated_by      VARCHAR(200)
        );

        CREATE UNIQUE INDEX IF NOT EXISTS uq_secret_key
        ON pragmatic_secrets (key, COALESCE(tenant_id, ''));

        CREATE INDEX IF NOT EXISTS ix_config_tenant ON pragmatic_config (tenant_id) WHERE tenant_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS ix_config_env ON pragmatic_config (environment) WHERE environment IS NOT NULL;

        -- Push notifications for the native watch (LISTEN/NOTIFY). The trigger fires on every write and
        -- emits the changed key/tenant/env plus the operation, so a listener also learns about DELETEs
        -- (which polling on updated_at can never observe). Idempotent: CREATE OR REPLACE + drop-then-create.
        CREATE OR REPLACE FUNCTION pragmatic_config_notify() RETURNS trigger AS $body$
        DECLARE
            row_data RECORD;
            op TEXT;
        BEGIN
            IF (TG_OP = 'DELETE') THEN
                row_data := OLD;
                op := 'delete';
            ELSE
                row_data := NEW;
                op := 'upsert';
            END IF;
            PERFORM pg_notify(
                'pragmatic_config_change',
                json_build_object(
                    'key', row_data.key,
                    'tenant', row_data.tenant_id,
                    'env', row_data.environment,
                    'op', op
                )::text);
            RETURN NULL;
        END;
        $body$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS trg_pragmatic_config_notify ON pragmatic_config;
        CREATE TRIGGER trg_pragmatic_config_notify
        AFTER INSERT OR UPDATE OR DELETE ON pragmatic_config
        FOR EACH ROW EXECUTE FUNCTION pragmatic_config_notify();
        """;
}

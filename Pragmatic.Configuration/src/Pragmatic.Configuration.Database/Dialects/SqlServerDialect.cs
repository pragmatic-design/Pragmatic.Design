namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     SQL Server dialect with MERGE, NVARCHAR, and VARBINARY support.
/// </summary>
internal sealed class SqlServerDialect : ISqlDialect
{
    /// <inheritdoc />
    /// <remarks>Uses backslash as ESCAPE character. Escapes \, %, _, and [ metacharacters.</remarks>
    public string EscapeLikePattern(string prefix)
        => prefix
            .Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal)
            .Replace("[", @"\[", StringComparison.Ordinal);

    /// <summary>DATETIMEOFFSET has sub-second precision; the native DateTimeOffset cursor compares precisely.</summary>
    public object FormatChangeCursor(DateTimeOffset cursor) => cursor;

    public string GetValue => """
        SELECT value FROM pragmatic_config
        WHERE [key] = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        """;

    public string SetValue => """
        MERGE pragmatic_config WITH (HOLDLOCK) AS target
        USING (SELECT @key AS [key], @tenantId AS tenant_id, @environment AS environment) AS source
        ON target.[key] = source.[key]
           AND (target.tenant_id = source.tenant_id OR (target.tenant_id IS NULL AND source.tenant_id IS NULL))
           AND (target.environment = source.environment OR (target.environment IS NULL AND source.environment IS NULL))
        WHEN MATCHED THEN
            UPDATE SET value = @value, version = target.version + 1,
                       updated_at = SYSUTCDATETIME(), updated_by = @updatedBy
        WHEN NOT MATCHED THEN
            INSERT ([key], value, tenant_id, environment, version, updated_by)
            VALUES (@key, @value, @tenantId, @environment, 1, @updatedBy);
        """;

    public string DeleteValue => """
        DELETE FROM pragmatic_config
        WHERE [key] = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        """;

    public string GetSection => """
        SELECT [key], value FROM pragmatic_config
        WHERE [key] LIKE @escapedPrefix + '%' ESCAPE '\'
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        ORDER BY [key]
        """;

    public string GetSecret => """
        SELECT encrypted_value FROM pragmatic_secrets
        WHERE [key] = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
        """;

    public string SetSecret => """
        MERGE pragmatic_secrets WITH (HOLDLOCK) AS target
        USING (SELECT @key AS [key], @tenantId AS tenant_id) AS source
        ON target.[key] = source.[key]
           AND (target.tenant_id = source.tenant_id OR (target.tenant_id IS NULL AND source.tenant_id IS NULL))
        WHEN MATCHED THEN
            UPDATE SET encrypted_value = @encryptedValue,
                       updated_at = SYSUTCDATETIME(), updated_by = @updatedBy
        WHEN NOT MATCHED THEN
            INSERT ([key], encrypted_value, tenant_id, updated_by)
            VALUES (@key, @encryptedValue, @tenantId, @updatedBy);
        """;

    public string DeleteSecret => """
        DELETE FROM pragmatic_secrets
        WHERE [key] = @key
          AND (tenant_id = @tenantId OR (tenant_id IS NULL AND @tenantId IS NULL))
        """;

    public string GetAllSecrets => "SELECT [key], encrypted_value, tenant_id FROM pragmatic_secrets";

    public string GetChangesSince => """
        SELECT [key], value, tenant_id, updated_at FROM pragmatic_config
        WHERE updated_at > @since
          AND (environment = @environment OR (environment IS NULL AND @environment IS NULL))
        ORDER BY updated_at
        """;

    public string CreateSchema => """
        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'pragmatic_config')
        CREATE TABLE pragmatic_config (
            id              BIGINT IDENTITY(1,1) PRIMARY KEY,
            [key]           NVARCHAR(500)   NOT NULL,
            value           NVARCHAR(MAX)   NOT NULL,
            tenant_id       NVARCHAR(100),
            environment     NVARCHAR(100),
            version         INT             NOT NULL DEFAULT 1,
            created_at      DATETIMEOFFSET  NOT NULL DEFAULT SYSUTCDATETIME(),
            updated_at      DATETIMEOFFSET  NOT NULL DEFAULT SYSUTCDATETIME(),
            updated_by      NVARCHAR(200),
            CONSTRAINT uq_config_key UNIQUE ([key], tenant_id, environment)
        );

        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'pragmatic_secrets')
        CREATE TABLE pragmatic_secrets (
            id              BIGINT IDENTITY(1,1) PRIMARY KEY,
            [key]           NVARCHAR(500)   NOT NULL,
            encrypted_value VARBINARY(MAX)  NOT NULL,
            tenant_id       NVARCHAR(100),
            created_at      DATETIMEOFFSET  NOT NULL DEFAULT SYSUTCDATETIME(),
            updated_at      DATETIMEOFFSET  NOT NULL DEFAULT SYSUTCDATETIME(),
            updated_by      NVARCHAR(200),
            CONSTRAINT uq_secret_key UNIQUE ([key], tenant_id)
        );


        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'ix_config_tenant')
        CREATE INDEX ix_config_tenant ON pragmatic_config ([key], tenant_id);

        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'ix_config_env')
        CREATE INDEX ix_config_env ON pragmatic_config ([key], environment);

        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'ix_audit_entity')

        IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'ix_audit_time')
        """;
}

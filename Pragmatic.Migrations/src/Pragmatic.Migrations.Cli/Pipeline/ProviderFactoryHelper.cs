using Pragmatic.Migrations.Introspection;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Cli.Pipeline;

/// <summary>
///     Helper to create provider-specific instances without DI container.
/// </summary>
internal static class ProviderFactoryHelper
{
    /// <summary>
    ///     Mirrors the exclusion list <see cref="Pragmatic.Migrations.Configuration.MigrationsBuilder" />
    ///     gives the host's introspector. Without it the CLI introspects tables the runner never sees —
    ///     the audit table under a custom name above all — and shows the operator a <c>DROP TABLE</c>
    ///     that the run will not perform.
    /// </summary>
    internal static ISchemaIntrospector CreateIntrospector(
        string? providerName, string? auditTableName = null)
    {
        string[] excluded =
        [
            auditTableName ?? MigrationConstants.AuditTableName,
            MigrationConstants.AuditTableName,
            MigrationConstants.EfMigrationsTableName,
            MigrationConstants.DataMigrationTableName,
            MigrationConstants.LockTableName
        ];

        return providerName switch
        {
            MigrationConstants.ProviderPostgreSql => new PostgreSqlSchemaIntrospector { ExcludedTables = excluded },
            MigrationConstants.ProviderSqlServer => new SqlServerSchemaIntrospector { ExcludedTables = excluded },
            MigrationConstants.ProviderSqlite => new SqliteSchemaIntrospector { ExcludedTables = excluded },
            // No silent PostgreSQL fallback: introspecting SQL Server with pg_catalog queries fails
            // deep inside the run with an error that names neither the provider nor the cause.
            _ => throw UnknownProvider(providerName)
        };
    }

    internal static ISqlMigrationGenerator CreateGenerator(string? providerName) => providerName switch
    {
        MigrationConstants.ProviderPostgreSql => new PostgreSqlMigrationGenerator(),
        MigrationConstants.ProviderSqlServer => new SqlServerMigrationGenerator(),
        MigrationConstants.ProviderSqlite => new SqliteMigrationGenerator(),
        _ => throw UnknownProvider(providerName)
    };

    internal static NotSupportedException UnknownProvider(string? providerName) =>
        new($"Unknown database provider '{providerName ?? "(none)"}'. The schema metadata must declare one of: " +
            $"{MigrationConstants.ProviderPostgreSql}, {MigrationConstants.ProviderSqlServer}, {MigrationConstants.ProviderSqlite}.");

    internal static IConnectionFactory CreateConnectionFactory(string? providerName)
    {
        var name = providerName ?? MigrationConstants.ProviderPostgreSql;

        // Use direct constructors — the CLI project already references all three provider packages.
        // This avoids runtime reflection (Type.GetType + Activator.CreateInstance).
        Func<string, System.Data.Common.DbConnection> factory = name switch
        {
            MigrationConstants.ProviderPostgreSql => cs => new Npgsql.NpgsqlConnection(cs),
            MigrationConstants.ProviderSqlServer  => cs => new Microsoft.Data.SqlClient.SqlConnection(cs),
            MigrationConstants.ProviderSqlite     => cs => new Microsoft.Data.Sqlite.SqliteConnection(cs),
            _ => throw UnknownProvider(providerName)
        };

        return new GenericConnectionFactory(name, factory);
    }
}

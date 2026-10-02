using Pragmatic.Audit.AdoNet;

namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     Picks the audit trail's SQL dialect from the same provider option as the configuration one.
/// </summary>
/// <remarks>
///     Two dialect choices driven by one setting, so a deployment cannot end up writing configuration
///     with one provider's syntax and its audit with another's.
/// </remarks>
internal static class AuditDialectFactory
{
    public static IAuditSqlDialect Create(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.PostgreSql => new PostgresAuditDialect(),
        DatabaseProvider.SqlServer => new SqlServerAuditDialect(),
        DatabaseProvider.Sqlite => new SqliteAuditDialect(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider")
    };
}

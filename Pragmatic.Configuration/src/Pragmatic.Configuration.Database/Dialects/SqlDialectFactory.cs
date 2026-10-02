namespace Pragmatic.Configuration.Database.Dialects;

/// <summary>
///     Resolves the correct SQL dialect for the configured database provider.
/// </summary>
internal static class SqlDialectFactory
{
    public static ISqlDialect Create(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.PostgreSql => new PostgresDialect(),
        DatabaseProvider.SqlServer => new SqlServerDialect(),
        DatabaseProvider.Sqlite => new SqliteDialect(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported database provider")
    };
}

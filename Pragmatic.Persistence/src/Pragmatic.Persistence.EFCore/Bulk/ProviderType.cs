namespace Pragmatic.Persistence.EFCore.Bulk;

/// <summary>
///     Database provider types supported by bulk operations.
/// </summary>
internal enum ProviderType
{
    /// <summary>Microsoft SQL Server.</summary>
    SqlServer,

    /// <summary>PostgreSQL via Npgsql.</summary>
    PostgreSql,

    /// <summary>SQLite.</summary>
    Sqlite,
}

namespace Pragmatic.SourceGenerator.Core;

/// <summary>
/// The EF Core database provider detected from referenced assemblies.
/// Used by the SG to generate provider-specific code (e.g., concurrency tokens).
/// </summary>
internal enum EfCoreProvider
{
    /// <summary>No specific provider detected — uses portable IsConcurrencyToken().</summary>
    Generic = 0,

    /// <summary>Npgsql (PostgreSQL) — uses xmin system column for concurrency.</summary>
    PostgreSql,

    /// <summary>SQL Server — uses native rowversion/timestamp type.</summary>
    SqlServer,

    /// <summary>SQLite — uses uint + IsConcurrencyToken() with manual increment.</summary>
    Sqlite
}

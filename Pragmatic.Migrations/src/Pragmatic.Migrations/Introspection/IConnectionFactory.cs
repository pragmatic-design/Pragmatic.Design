using System.Data.Common;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Creates database connections from connection strings.
///     Provider-specific implementations create the correct DbConnection subtype.
/// </summary>
public interface IConnectionFactory
{
    /// <summary>Database provider name (e.g. "PostgreSql", "SqlServer", "Sqlite").</summary>
    string ProviderName { get; }

    /// <summary>Creates and opens a connection to the database.</summary>
    Task<DbConnection> CreateOpenConnectionAsync(string connectionString, CancellationToken ct = default);
}

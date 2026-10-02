using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Pragmatic.Configuration.Database.Tests;

/// <summary>
///     In-memory SQLite connection factory for testing.
///     Uses a shared connection to keep the in-memory database alive across calls.
/// </summary>
internal sealed class SqliteConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _keepAlive;

    public SqliteConnectionFactory()
    {
        // Shared in-memory database — all connections see the same data
        var dbName = $"ConfigTest_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep one connection open to prevent the in-memory DB from being destroyed
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
    }

    public DbConnection CreateConnection() => new SqliteConnection(_connectionString);

    public void Dispose()
    {
        _keepAlive?.Dispose();
        _keepAlive = null;
    }
}

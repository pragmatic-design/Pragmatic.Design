using System.Data.Common;
using Microsoft.Data.Sqlite;
using Pragmatic.Configuration.Database;

namespace Pragmatic.Configuration.Samples.Samples;

/// <summary>
///     In-memory SQLite <see cref="IDbConnectionFactory"/> for the database samples.
///     A single keep-alive connection holds the shared in-memory database open so that
///     every connection created by the store sees the same data and auto-created schema.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory, IDisposable
{
    private readonly string _connectionString;
    private SqliteConnection? _keepAlive;

    public SqliteConnectionFactory()
    {
        // Unique shared in-memory database name so parallel samples never collide.
        var dbName = $"ConfigSample_{Guid.NewGuid():N}";
        _connectionString = $"Data Source={dbName};Mode=Memory;Cache=Shared";

        // Keep one connection open for the lifetime of the factory: an in-memory SQLite
        // database is destroyed once its last connection closes.
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

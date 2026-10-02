using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Temporal.EFCore.Tests;

/// <summary>
///     Creates in-memory Sqlite contexts. The connection must stay open for the
///     database lifetime, so it is returned for disposal by the caller.
/// </summary>
internal static class SqliteContextFactory
{
    public static (SqliteConnection Connection, DbContextOptions<TContext> Options) CreateOptions<TContext>(
        Action<DbContextOptionsBuilder<TContext>>? configure = null)
        where TContext : DbContext
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var builder = new DbContextOptionsBuilder<TContext>().UseSqlite(connection);
        configure?.Invoke(builder);

        return (connection, builder.Options);
    }
}

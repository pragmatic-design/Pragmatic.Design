using Pragmatic.Persistence.Sql.Npgsql;
using Pragmatic.Persistence.Sql.SqlClient;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The bulk load behind <c>InsertManyAsync</c>: a session with the provider's copier (<c>COPY</c> on PostgreSQL, <c>SqlBulkCopy</c> on SQL
///     Server) loads a batch at or above <c>BulkThreshold</c> through it, with the same values the INSERT would bind, inside the session's
///     transaction; below the threshold, or without a copier (SQLite), the multi-row INSERT runs as before.
/// </summary>
[Collection("databases")]
public class BulkTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static ISqlBulkCopier? CopierFor(SqlDialect dialect) => dialect switch
    {
        SqlDialect.PostgreSql => new NpgsqlBulkCopier(),
        SqlDialect.SqlServer => new SqlClientBulkCopier(),
        _ => null,
    };

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_batch_at_the_threshold_goes_through_the_copier_with_the_same_values(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = new SqlSession(databases.Open(dialect), dialect) { BulkCopier = CopierFor(dialect), BulkThreshold = 200 };
        var orderId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 20, 7, 30, 0, TimeSpan.Zero);
        Event Row(int i) => new() { Id = Guid.NewGuid(), OrderId = orderId, Kind = i % 2 == 0 ? "even" : "odd", At = at.AddSeconds(i), Version = i };

        // At the threshold: the copier (where there is one); the rows read back are the rows written, every column.
        var rows = Enumerable.Range(0, 600).Select(Row).ToList();
        Assert.Equal(600, await session.InsertManyAsync(rows));
        var back = await session.Query<Event>().Where((e, id) => e.OrderId == id, orderId).OrderBy(e => e.At).ToListAsync();
        Assert.Equal(rows.Select(r => (r.Id, r.Kind, r.At, r.Version)), back.Select(r => (r.Id, r.Kind, r.At, r.Version)));

        // Below the threshold: the INSERT path, same outcome. Inside a transaction rolled back: nothing stays, either way.
        Assert.Equal(50, await session.InsertManyAsync(Enumerable.Range(600, 50).Select(Row).ToList()));
        using (var transaction = session.BeginTransaction())
        {
            Assert.Equal(300, await session.InsertManyAsync(Enumerable.Range(1000, 300).Select(Row).ToList()));
            Assert.Equal(950, await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId));
            transaction.Rollback();
        }

        Assert.Equal(650, await session.Query<Event>().CountAsync((e, id) => e.OrderId == id, orderId));
        await session.Query<Event>().Where((e, id) => e.OrderId == id, orderId).ExecuteDeleteAsync();
    }
}

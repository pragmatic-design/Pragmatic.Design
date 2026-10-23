using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Sequences (<c>session.NextValueAsync</c>, <c>NextValuesAsync</c>): a sequence on PostgreSQL and SQL Server, the counter table on
///     SQLite, created by the schema with <see cref="SqlSequences.CreateSql" />; values unique and increasing across sessions, a range
///     in one round trip, inside a transaction too.
/// </summary>
[Collection("databases")]
public class SequenceTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Fact]
    public void The_ddl_and_the_statements_render_per_dialect()
    {
        Assert.Equal(["CREATE SEQUENCE IF NOT EXISTS \"invoice_numbers\" START WITH 1"], SqlSequences.CreateSql(SqlDialect.PostgreSql, "invoice_numbers"));
        Assert.Equal(["IF NOT EXISTS (SELECT 1 FROM sys.sequences WHERE name = N'invoice_numbers') CREATE SEQUENCE [invoice_numbers] AS bigint START WITH 500 INCREMENT BY 1"], SqlSequences.CreateSql(SqlDialect.SqlServer, "invoice_numbers", 500));
        Assert.Equal(2, SqlSequences.CreateSql(SqlDialect.Sqlite, "invoice_numbers").Count);
        Assert.Throws<ArgumentException>(() => SqlSequences.CreateSql(SqlDialect.PostgreSql, "invoice numbers; DROP TABLE x"));
        Assert.Throws<ArgumentException>(() => SqlSequences.CreateSql(SqlDialect.PostgreSql, ""));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Values_are_unique_increasing_and_ranged(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        using var other = databases.Session(dialect);

        var first = await session.NextValueAsync(ExtrasSchema.TicketNumbers);
        var second = await session.NextValueAsync(ExtrasSchema.TicketNumbers);
        var elsewhere = await other.NextValueAsync(ExtrasSchema.TicketNumbers);
        Assert.True(first >= 1000, first.ToString());
        Assert.True(second > first, $"{first} {second}");
        Assert.True(elsewhere > second, $"{second} {elsewhere}");

        // A range: as many values as asked, increasing, none handed out before; the next single value after them.
        var range = await session.NextValuesAsync(ExtrasSchema.TicketNumbers, 5);
        Assert.Equal(5, range.Length);
        Assert.True(range[0] > elsewhere, $"{elsewhere} {range[0]}");
        for (var i = 1; i < range.Length; i++)
            Assert.True(range[i] > range[i - 1], string.Join(",", range));
        Assert.True(await session.NextValueAsync(ExtrasSchema.TicketNumbers) > range[^1]);

        // Inside a transaction: the value is taken through it; rolled back, a sequence keeps it consumed (a gap, never a repeat), while
        // SQLite's counter row is rolled back with the transaction and hands the same value out again (the emulation's one difference).
        long inside;
        using (var transaction = session.BeginTransaction())
        {
            inside = await session.NextValueAsync(ExtrasSchema.TicketNumbers);
            transaction.Rollback();
        }

        var after = await session.NextValueAsync(ExtrasSchema.TicketNumbers);
        if (dialect is SqlDialect.Sqlite or SqlDialect.MySql) // the counter table rolls back with the transaction on both
            Assert.Equal(inside, after);
        else
            Assert.True(after > inside, $"{inside} {after}");

        // A sequence that is not there.
        var missing = await Assert.ThrowsAnyAsync<Exception>(() => session.NextValueAsync("no_such_sequence").AsTask());
        Assert.NotNull(missing);
        await Assert.ThrowsAsync<ArgumentException>(() => session.NextValueAsync("bad name").AsTask());
    }
}

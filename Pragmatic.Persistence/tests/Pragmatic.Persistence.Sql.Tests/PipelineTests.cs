using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Pipelining: where the provider creates batches (Npgsql, SqlClient), a <c>SqlBatch</c> and a <c>SqlWriteBatch</c> run as the
///     provider's <c>DbBatch</c>, one command per statement; the results and the counts are those of the joined-text form, which
///     Microsoft.Data.Sqlite keeps (no batches there) and <c>Pipelining = false</c> selects anywhere.
/// </summary>
[Collection("databases")]
public class PipelineTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Batches_answer_the_same_pipelined_and_as_one_text(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        using var pipelined = databases.Session(dialect);
        using var joined = databases.Session(dialect);
        Assert.False(pipelined.Pipelining); // off by default: measured no gain on PostgreSQL, a loss on SQL Server
        pipelined.Pipelining = true; // without effect on SQLite (no batches there): the same joined text as `joined`
        Assert.Equal(dialect is not (SqlDialect.Sqlite or SqlDialect.Oracle), pipelined.Connection.CanCreateBatch); // ODP.NET has no DbBatch: the joined text there too

        // Reads: the same six-item batch both ways, twice each (the second run reuses the kept batch).
        for (var run = 0; run < 2; run++)
        {
            var a = pipelined.Batch();
            var b = joined.Batch();
            var (rowsA, rowsB) = (a.OpenRows(customerId, 1 + run, 10), b.OpenRows(customerId, 1 + run, 10));
            var (countA, countB) = (a.CountOpenAbove(1000m + run), b.CountOpenAbove(1000m + run));
            var (linesA, linesB) = (a.WithLines(customerId), b.WithLines(customerId));
            await a.ExecuteAsync();
            await b.ExecuteAsync();
            Assert.Equal(rowsB.Result.Select(r => (r.Id, r.CustomerName)), rowsA.Result.Select(r => (r.Id, r.CustomerName)));
            Assert.Equal(countB.Result, countA.Result);
            Assert.Equal(linesB.Result.Select(o => (o.Id, o.Lines.Count)), linesA.Result.Select(o => (o.Id, o.Lines.Count)));
        }

        // Writes: a mixed write batch (insert, save, delete, an orphan delete with a key list: a text that varies) both ways, the counts checked statement by statement.
        foreach (var session in new[] { pipelined, joined })
        {
            var invoice = new Invoice { Id = Guid.NewGuid(), Number = "PIPE-" + session.Pipelining, Total = 1m, IsNew = true };
            invoice.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "A", Quantity = 1, IsNew = true });
            invoice.AddLine(new InvoiceLine { Id = Guid.NewGuid(), Sku = "B", Quantity = 2, IsNew = true });
            Assert.Equal(3, await session.SaveGraphAsync(invoice));
            invoice.RemoveLine(invoice.Lines[0]);
            invoice.SetTotal(2m);
            Assert.Equal(2, await session.SaveGraphAsync(invoice)); // the total, the orphan line
            var back = await session.Query<Invoice>().Where((i, id) => i.Id == id, invoice.Id).Include(i => i.Lines).FirstAsync();
            Assert.Equal((2m, 1, "B"), (back.Total, back.Lines.Count, back.Lines[0].Sku));
            var stale = new Invoice { Id = invoice.Id, Number = invoice.Number, Total = 9m, Version = 0 };
            await Assert.ThrowsAsync<SqlConcurrencyException>(() => session.WriteBatch().Update(stale).ExecuteAsync().AsTask());
            await session.DeleteGraphAsync(invoice);
            Assert.Equal(0, await session.Query<Invoice>().CountAsync((i, id) => i.Id == id, invoice.Id));
        }
    }
}

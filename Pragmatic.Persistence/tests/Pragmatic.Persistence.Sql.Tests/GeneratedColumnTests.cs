using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     <c>[DatabaseGenerated]</c>: a computed column and a default are never written (the object's values are ignored, the database's
///     stand) and come back by key into the object after an insert, one row or many, after an update for the column marked OnUpdate, after
///     an upsert, a save and in a write batch. In a transaction, rolled back.
/// </summary>
[Collection("databases")]
public class GeneratedColumnTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Generated_columns_are_left_to_the_database_and_read_back(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();

        // One row: the object's Doubled and Code are not written (the default and the computation stand), and come back.
        var gauge = new Gauge { Id = Guid.NewGuid(), Quantity = 3, Doubled = -1, Code = "mine" };
        await session.InsertAsync(gauge);
        Assert.Equal((6, "NEW"), (gauge.Doubled, gauge.Code));

        // An update recomputes Doubled (OnUpdate): read back too; Code stays the database's.
        gauge.Quantity = 5;
        gauge.Code = "changed";
        await session.UpdateAsync(gauge);
        Assert.Equal((10, "NEW"), (gauge.Doubled, gauge.Code));

        // Many rows in one INSERT: each row's values come back into its object.
        var many = Enumerable.Range(1, 4).Select(q => new Gauge { Id = Guid.NewGuid(), Quantity = q }).ToList();
        await session.InsertManyAsync(many);
        Assert.Equal([2, 4, 6, 8], many.Select(g => g.Doubled));
        Assert.All(many, g => Assert.Equal("NEW", g.Code));

        // More rows than one read takes (150: two SELECTs, each row tagged with its position): every object gets its own row's values.
        var lot = Enumerable.Range(1, 150).Select(q => new Gauge { Id = Guid.NewGuid(), Quantity = q }).ToList();
        await session.InsertManyAsync(lot);
        Assert.Equal(lot.Select(g => g.Quantity * 2), lot.Select(g => g.Doubled));

        // A write batch leaves the columns out as well and reads them back after its command: an insert, and an update of OnUpdate's source.
        var batched = new Gauge { Id = Guid.NewGuid(), Quantity = 7, Code = "mine" };
        gauge.Quantity = 6;
        await session.WriteBatch().Insert(batched).Update(gauge).ExecuteAsync();
        Assert.Equal((14, "NEW", 12), (batched.Doubled, batched.Code, gauge.Doubled));
        var read = await session.Query<Gauge>().FirstOrDefaultAsync((g, id) => g.Id == id, batched.Id);
        Assert.Equal((14, "NEW"), (read!.Doubled, read.Code));

        // An upsert reads them back, inserted or updated.
        var upserted = new Gauge { Id = Guid.NewGuid(), Quantity = 4, Doubled = -1, Code = "mine" };
        await session.UpsertAsync(upserted);
        Assert.Equal((8, "NEW"), (upserted.Doubled, upserted.Code));
        upserted.Quantity = 9;
        await session.UpsertAsync(upserted);
        Assert.Equal(18, upserted.Doubled);

        // A save of a modified quantity (its own UPDATE of that column): the recomputed column comes back; a new row's save is an insert.
        var tracked = new Gauge { Id = Guid.NewGuid(), Quantity = 1, IsNew = true };
        await session.SaveAsync(tracked);
        Assert.Equal((2, "NEW"), (tracked.Doubled, tracked.Code));
        tracked.SetQuantity(11);
        await session.SaveAsync(tracked);
        Assert.Equal(22, tracked.Doubled);
        var second = new Gauge { Id = Guid.NewGuid(), Quantity = 2, IsNew = true };
        await session.SaveManyAsync([second]);
        second.SetQuantity(3);
        tracked.SetQuantity(12);
        Assert.Equal(2, await session.SaveManyAsync([tracked, second])); // a run of the same modified column, then each row read back
        Assert.Equal((24, 6), (tracked.Doubled, second.Doubled));
        transaction.Rollback();
    }
}

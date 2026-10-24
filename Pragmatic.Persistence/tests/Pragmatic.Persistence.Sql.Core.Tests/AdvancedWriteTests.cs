using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>
///     The advanced write forms, each on the three databases with the row read back: an update or delete whose predicate joins a
///     navigation (<c>UPDATE … FROM</c>, <c>DELETE … USING</c>, an EXISTS on SQLite) and whose rows come back (<c>RETURNING</c>,
///     <c>OUTPUT</c>), <c>INSERT INTO … SELECT</c> from a chain, and the MERGE of a set with given rows (<c>SyncAsync</c>: update, insert,
///     delete in one statement; an upsert and a delete on SQLite). Everything inside a transaction rolled back: the seed stays.
/// </summary>
[Collection("databases")]
public class AdvancedWriteTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Update_and_delete_through_a_navigation_read_their_rows_back(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var seed = databases.Seed;
        using var transaction = session.BeginTransaction();

        // UPDATE … FROM: the predicate joins Customers; the rows come back as they are after the update.
        var italian = seed.Orders.Where(o => !o.IsDeleted && seed.Customers.First(c => c.Id == o.CustomerId).Country == "IT").Select(o => o.Id).ToHashSet();
        if (SqlDialects.NoReturning(dialect))
        {
            // No RETURNING or OUTPUT rows on MySQL and Oracle: the plan says so (and PRAG3012 did at compile time); the forms without rows read back are below.
            var refused = await Assert.ThrowsAsync<NotSupportedException>(async () => await session.Query<Order>().Where((o, country) => o.Customer!.Country == country && !o.IsDeleted, "IT").ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), o => o));
            Assert.Contains("RETURNING", refused.Message, StringComparison.Ordinal);
            var updated = await session.Query<Order>().Where((o, country) => o.Customer!.Country == country && !o.IsDeleted, "IT").ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid));
            Assert.Equal(italian.Count, updated);
            var target = seed.Orders[321];
            Assert.Equal(Seed.LinesPerOrder, await session.Query<OrderLine>().Where((l, number) => l.Order!.Number == number, target.Number).ExecuteDeleteAsync());
            Assert.Equal(0, await session.Query<OrderLine>().CountAsync((l, id) => l.OrderId == id, target.Id));
            transaction.Rollback();
            return;
        }

        var paid = await session.Query<Order>().Where((o, country) => o.Customer!.Country == country && !o.IsDeleted, "IT").ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid), o => o);
        Assert.Equal(italian.Count, paid.Count);
        Assert.Equal(italian, paid.Select(o => o.Id).ToHashSet());
        Assert.All(paid, o => Assert.Equal(OrderStatus.Paid, o.Status));
        Assert.Equal(italian.Count, await session.Query<Order>().CountAsync((o, country) => o.Customer!.Country == country && !o.IsDeleted && o.Status == OrderStatus.Paid, "IT"));

        // A SET from the joined row, a DTO read back.
        var relabelled = await session.Query<Order>().Where((o, country) => o.Customer!.Country == country && !o.IsDeleted, "DE").ExecuteUpdateAsync(s => s.SetProperty(o => o.Number, o => o.Customer!.Country), o => new OrderTotalDto { Id = o.Id, Number = o.Number, Total = o.Total });
        Assert.NotEmpty(relabelled);
        Assert.All(relabelled, r => Assert.Equal("DE", r.Number));

        // DELETE through a navigation, the keys read back; the lines are gone.
        var order = seed.Orders[321];
        var deleted = await session.Query<OrderLine>().Where((l, number) => l.Order!.Number == number, order.Number).ExecuteDeleteAsync(l => l.Id);
        Assert.Equal(Seed.LinesPerOrder, deleted.Count);
        Assert.Equal(0, await session.Query<OrderLine>().CountAsync((l, id) => l.OrderId == id, order.Id));

        transaction.Rollback();
        Assert.Equal(Seed.LinesPerOrder, await session.Query<OrderLine>().CountAsync((l, id) => l.OrderId == id, order.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Insert_select_copies_the_chains_rows_into_another_table(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var seed = databases.Seed;
        using var transaction = session.BeginTransaction();

        var customer = seed.BusyCustomer;
        var inserted = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customer.Id).ExecuteInsertAsync(o => new Note { OrderId = o.Id, Text = "copied" });
        var expected = seed.Orders.Count(o => o.CustomerId == customer.Id && !o.IsDeleted);
        Assert.Equal(expected, inserted);
        Assert.Equal(expected, await session.Query<Note>().CountAsync((n, text) => n.Text == text, "copied"));
        var one = seed.Orders.First(o => o.CustomerId == customer.Id && !o.IsDeleted);
        var note = await session.Query<Note>().FirstOrDefaultAsync((n, id) => n.OrderId == id, one.Id);
        Assert.NotNull(note);
        Assert.Equal("copied", note.Text);
        Assert.True(note.Id > 0); // the key the database assigned

        // Through a navigation in the predicate too (a join in the SELECT).
        var byCountry = await session.Query<Order>().Where((o, country) => o.Customer!.Country == country, "NL").ExecuteInsertAsync(o => new Note { OrderId = o.Id, Text = "nl" });
        Assert.Equal(seed.Orders.Count(o => seed.Customers.First(c => c.Id == o.CustomerId).Country == "NL"), byCountry);
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Sync_merges_a_set_with_the_given_rows(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var seed = databases.Seed;
        using var transaction = session.BeginTransaction();

        var order = seed.Orders[123];
        var lines = await session.Query<OrderLine>().Where((l, id) => l.OrderId == id, order.Id).OrderBy(l => l.Sku).ThenBy(l => l.Id).ToListAsync();
        Assert.Equal(Seed.LinesPerOrder, lines.Count);

        // The set becomes: the first line with a new quantity, the third as it is, a new fourth; the second is not among them.
        var kept = lines[0];
        kept.Quantity += 10;
        var added = new OrderLine { Id = Guid.NewGuid(), OrderId = order.Id, Sku = "SYNC-1", Quantity = 1, UnitPrice = 9.99m };
        var affected = await session.Query<OrderLine>().Where((l, id) => l.OrderId == id, order.Id).SyncAsync([kept, lines[2], added]);
        Assert.Equal(4, affected); // two matched rows updated, one inserted, one deleted: four actions on every dialect (the upsert and the delete together on SQLite)

        var after = await session.Query<OrderLine>().Where((l, id) => l.OrderId == id, order.Id).ToListAsync();
        Assert.Equal(new[] { kept.Id, lines[2].Id, added.Id }.ToHashSet(), after.Select(l => l.Id).ToHashSet());
        Assert.Equal(kept.Quantity, after.First(l => l.Id == kept.Id).Quantity);
        Assert.Equal(9.99m, after.First(l => l.Id == added.Id).UnitPrice);
        Assert.DoesNotContain(after, l => l.Id == lines[1].Id);

        // The other orders' lines are untouched (the set is the predicate's).
        Assert.Equal(Seed.LinesPerOrder, await session.Query<OrderLine>().CountAsync((l, id) => l.OrderId == id, seed.Orders[124].Id));
        transaction.Rollback();
    }
}

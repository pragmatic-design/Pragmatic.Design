using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     <c>[RollUp&lt;TChild&gt;]</c>: a cart's units (sum), line count (count) and amount (sum of a money column) kept by its lines' writes, every path:
///     insert, many, update, save (a quantity, a line moved to another cart), upsert, delete, a write batch, a graph save with its orphans, a graph
///     delete. After each step the stored aggregates match the lines; the cart's own save leaves them alone. In a transaction, rolled back; the
///     last test without one (each write then takes its own), cleaning up after itself.
/// </summary>
[Collection("databases")]
public class RollUpTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task The_lines_writes_keep_the_carts_aggregates(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();

        // The cart's insert leaves the aggregates to the database's default and reads them back.
        var cart = new Cart { Id = Guid.NewGuid(), Name = "a", Units = 99, LineCount = 99, Amount = 99m, IsNew = true };
        var other = new Cart { Id = Guid.NewGuid(), Name = "b", IsNew = true };
        await session.InsertManyAsync(new[] { cart, other });
        Assert.Equal((0, 0, 0m), (cart.Units, cart.LineCount, cart.Amount));

        var first = Line(cart, 2, 1.25m);
        await session.InsertAsync(first);
        await Expect(session, cart, (2, 1, 1.25m));

        var three = new[] { Line(cart, 1, 2.5m), Line(cart, 3, 0.75m), Line(other, 5, 10m) };
        await session.InsertManyAsync(three);
        await Expect(session, cart, (6, 3, 4.5m));
        await Expect(session, other, (5, 1, 10m));

        first.Quantity = 4;
        await session.UpdateAsync(first);
        await Expect(session, cart, (8, 3, 4.5m));

        // A save of the quantity moves the units; a line moved to the other cart leaves one and joins the other.
        foreach (var row in three)
            row.IsNew = false; // inserted: a save now updates them
        three[0].ResetModifiedProperties();
        three[0].SetQuantity(6);
        await session.SaveAsync(three[0]);
        await Expect(session, cart, (13, 3, 4.5m));
        three[1].ResetModifiedProperties();
        three[1].MoveTo(other.Id);
        await session.SaveAsync(three[1]);
        await Expect(session, cart, (10, 2, 3.75m));
        await Expect(session, other, (8, 2, 10.75m));

        // An upsert: a new line, then the same line changed.
        var upserted = Line(cart, 7, 1m);
        await session.UpsertAsync(upserted);
        await Expect(session, cart, (17, 3, 4.75m));
        upserted.Quantity = 1;
        await session.UpsertAsync(upserted);
        await Expect(session, cart, (11, 3, 4.75m));

        await session.DeleteAsync(upserted);
        await Expect(session, cart, (10, 2, 3.75m));

        // A write batch: an insert, an update and a delete in one command.
        var batched = Line(cart, 2, 2m);
        first.Quantity = 1;
        await session.WriteBatch().Insert(batched).Update(first).Delete(three[2]).ExecuteAsync();
        await Expect(session, cart, (9, 3, 5.75m));
        await Expect(session, other, (3, 1, 0.75m));

        // The cart's own save (its name) leaves the aggregates as the lines made them.
        cart.IsNew = false;
        cart.ResetModifiedProperties();
        cart.SetName("renamed");
        await session.SaveAsync(cart);
        await Expect(session, cart, (9, 3, 5.75m));

        // A graph save: a new cart with its lines (the aggregates read back into the object), then a line removed (an orphan) and one added.
        var graph = new Cart { Id = Guid.NewGuid(), Name = "graph", IsNew = true };
        var kept = new CartLine { Id = Guid.NewGuid(), Quantity = 2, Price = 3m, IsNew = true };
        var dropped = new CartLine { Id = Guid.NewGuid(), Quantity = 5, Price = 1m, IsNew = true };
        graph.AddLine(kept);
        graph.AddLine(dropped);
        await session.SaveGraphAsync(graph);
        Assert.Equal((7, 2, 4m), (graph.Units, graph.LineCount, graph.Amount));
        graph.RemoveLine(dropped);
        graph.AddLine(new CartLine { Id = Guid.NewGuid(), Quantity = 1, Price = 0.5m, IsNew = true });
        await session.SaveGraphAsync(graph);
        await Expect(session, graph, (3, 2, 3.5m));

        // A graph delete of a line's cart, and of a line on its own: the cascade's rows subtracted before their DELETE.
        await session.DeleteGraphAsync(batched);
        await Expect(session, cart, (7, 2, 3.75m));
        await session.DeleteGraphAsync(graph);
        Assert.Null(await session.Query<Cart>().FirstOrDefaultAsync((c, id) => c.Id == id, graph.Id));
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Without_a_transaction_each_write_takes_one_of_its_own(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        using var session = databases.Session(dialect);
        var cart = new Cart { Id = Guid.NewGuid(), Name = "own", IsNew = true };
        await session.InsertAsync(cart);
        try
        {
            var line = Line(cart, 3, 2m);
            await session.InsertAsync(line);
            await session.InsertManyAsync(new[] { Line(cart, 1, 1m), Line(cart, 1, 1m) });
            line.Quantity = 5;
            await session.UpdateAsync(line);
            await Expect(session, cart, (7, 3, 4m));
            await session.DeleteAsync(line);
            await Expect(session, cart, (2, 2, 2m));
            Assert.Null(session.Transaction);
        }
        finally
        {
            await session.DeleteGraphAsync(cart);
        }
    }

    private static CartLine Line(Cart cart, int quantity, decimal price) => new() { Id = Guid.NewGuid(), CartId = cart.Id, Quantity = quantity, Price = price, IsNew = true };

    /// <summary>The cart's stored aggregates as expected, and equal to the ones its lines give now.</summary>
    private static async Task Expect(SqlSession session, Cart cart, (int Units, int Lines, decimal Amount) expected)
    {
        var stored = await session.Query<Cart>().FirstOrDefaultAsync((c, id) => c.Id == id, cart.Id);
        Assert.NotNull(stored);
        var lines = await session.Query<CartLine>().Where((l, id) => l.CartId == id, cart.Id).ToListAsync();
        Assert.Equal((lines.Sum(l => l.Quantity), lines.Count, lines.Sum(l => l.Price)), (stored.Units, stored.LineCount, stored.Amount));
        Assert.Equal(expected, (stored.Units, stored.LineCount, stored.Amount));
    }
}

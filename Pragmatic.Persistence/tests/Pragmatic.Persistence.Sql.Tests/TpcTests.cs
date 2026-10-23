using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     A hierarchy with a table per concrete type (<c>[Inheritance(Strategy = Tpc)]</c>): the root read through the view the generator
///     writes (<c>PaymentView.CreateSql</c>), each derived type from its own table with no discriminator predicate, type tests and
///     OfType on the root's rows, and the writes through the root dispatched to the row's own table.
/// </summary>
[Collection("databases")]
public class TpcTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void The_root_reads_the_view_and_each_type_its_own_table()
    {
        var session = Session(SqlDialect.PostgreSql);

        // The root: the view, its discriminator a column of it; a derived type: its table, no predicate on a discriminator.
        Assert.Equal("SELECT t.\"Id\", t.\"OrderId\", t.\"Amount\", t.\"PaidAt\", t.\"Kind\", t.\"Last4\", t.\"Brand\", t.\"Iban\", t.\"Days\" FROM \"Payments\" t WHERE (t.\"Kind\" = 'CardPayment')", session.Query<Payment>().Where(p => p is CardPayment).ToSql());
        Assert.Equal("SELECT t.\"Id\", t.\"OrderId\", t.\"Amount\", t.\"PaidAt\", t.\"Last4\", t.\"Brand\" FROM \"CardPayments\" t WHERE (t.\"Amount\" > @p0)", session.Query<CardPayment>().Where(p => p.Amount > 0m).ToSql());
        Assert.Equal(session.Query<WirePayment>().Where(w => w.Days > 0).ToSql(), session.Query<Payment>().OfType<WirePayment>().Where(w => w.Days > 0).ToSql());
        Assert.EndsWith("FROM \"Payments\" t INNER JOIN \"Orders\" j_Order ON j_Order.\"Id\" = t.\"OrderId\" WHERE (j_Order.\"CustomerId\" = @p0)", session.Query<Payment>().Where((p, customer) => p.Order.CustomerId == customer, Guid.Empty).ToSql(), StringComparison.Ordinal);

        // The view's text: one SELECT per derived type in the root's column order, the type's name as Kind, NULL for the other type's columns.
        Assert.Equal("SELECT t.\"Id\", t.\"OrderId\", t.\"Amount\", t.\"PaidAt\", 'CardPayment' AS \"Kind\", t.\"Last4\", t.\"Brand\", NULL AS \"Iban\", NULL AS \"Days\" FROM \"CardPayments\" t UNION ALL SELECT t.\"Id\", t.\"OrderId\", t.\"Amount\", t.\"PaidAt\", 'WirePayment' AS \"Kind\", NULL AS \"Last4\", NULL AS \"Brand\", t.\"Iban\", t.\"Days\" FROM \"WirePayments\" t", PaymentView.SelectSql(SqlDialect.PostgreSql));
        Assert.StartsWith("CREATE OR ALTER VIEW [Payments] AS SELECT t.[Id], t.[OrderId]", PaymentView.CreateSql(SqlDialect.SqlServer), StringComparison.Ordinal);
        Assert.Equal("Payments", PaymentView.Name);

        // The root has no writer of its own: its map dispatches to the row's type.
        Assert.Null(SqlEntityMap<Payment>.Current.Writer);
        Assert.True(SqlEntityMap<Payment>.Current.Dispatch);
        Assert.NotNull(SqlEntityMap<CardPayment>.Current.Writer);
        Assert.False(SqlEntityMap<CardPayment>.Current.Dispatch);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Writes_through_the_root_go_to_the_row_type_and_reads_come_back_typed(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var orders = seed.Orders.Where(o => !o.IsDeleted).OrderBy(o => o.Id).Take(3).ToList();
        var paidAt = new DateTime(2026, 10, 20, 9, 30, 0, DateTimeKind.Unspecified);
        var payments = new List<Payment>
        {
            new CardPayment { Id = Guid.NewGuid(), OrderId = orders[0].Id, Amount = 10m, PaidAt = paidAt, Last4 = "1234", Brand = "visa" },
            new CardPayment { Id = Guid.NewGuid(), OrderId = orders[1].Id, Amount = 20m, PaidAt = paidAt, Last4 = "9999", Brand = "amex" },
            new WirePayment { Id = Guid.NewGuid(), OrderId = orders[2].Id, Amount = 30m, PaidAt = paidAt, Iban = "IT00X", Days = 2 },
        };
        try
        {
            // Inserts through the root: one batch, each row into its own table.
            await session.InsertAsync(payments[0]);
            Assert.Equal(2, await session.InsertManyAsync(payments.Skip(1).ToList()));
            Assert.Equal(2, await session.Query<CardPayment>().CountAsync());
            Assert.Equal(1, await session.Query<WirePayment>().CountAsync());

            // Reads through the root come back as the row's type, with its own columns.
            var all = await session.Query<Payment>().OrderBy(p => p.Amount).ToListAsync();
            Assert.Equal(3, all.Count);
            Assert.IsType<CardPayment>(all[0]);
            Assert.Equal("visa", ((CardPayment)all[0]).Brand);
            Assert.IsType<WirePayment>(all[2]);
            Assert.Equal(2, ((WirePayment)all[2]).Days);
            Assert.Equal(1, await session.Query<Payment>().CountAsync(p => p is WirePayment));
            Assert.Equal(2, await session.Query<Payment>().CountAsync(p => p is not WirePayment));
            Assert.Equal(["1234", "9999"], (await session.Query<Payment>().OfType<CardPayment>().OrderBy(c => c.Amount).Select(c => c.Last4).ToListAsync()));
            Assert.Equal(1, await session.Query<Payment>().CountAsync(p => p is CardPayment ? ((CardPayment)p).Brand == "amex" : ((WirePayment)p).Days > 5));

            // A navigation from the view: the order's customer.
            var customer = orders[0].CustomerId;
            var ofCustomer = await session.Query<Payment>().Where((p, c) => p.Order.CustomerId == c, customer).Select(p => p.Id).ToListAsync();
            Assert.Equal(payments.Where(p => orders.First(o => o.Id == p.OrderId).CustomerId == customer).Select(p => p.Id).OrderBy(id => id), ofCustomer.OrderBy(id => id));

            // An update and a delete through the root; an update through a derived type's chain.
            ((CardPayment)payments[1]).Brand = "mastercard";
            await session.UpdateAsync(payments[1]);
            Assert.Equal("mastercard", (await session.Query<CardPayment>().Where((c, id) => c.Id == id, payments[1].Id).Select(c => c.Brand).FirstAsync()));
            Assert.Equal(1, await session.Query<WirePayment>().Where(w => w.Days == 2).ExecuteUpdateAsync(s => s.SetProperty(w => w.Days, 3)));
            Assert.Equal(3, (await session.Query<Payment>().OfType<WirePayment>().Select(w => w.Days).FirstAsync()));
            await session.DeleteAsync(payments[2]);
            Assert.Equal(0, await session.Query<Payment>().CountAsync(p => p is WirePayment));

            // A batch mixing the types through the root; an upsert through the root goes to the row's own type, as the other writes do.
            var wire = new WirePayment { Id = Guid.NewGuid(), OrderId = orders[2].Id, Amount = 40m, PaidAt = paidAt, Iban = "IT11Y", Days = 1 };
            payments.Add(wire);
            await session.WriteBatch().Insert<Payment>(wire).Delete<Payment>(payments[0]).ExecuteAsync();
            Assert.Equal(2, await session.Query<Payment>().CountAsync());
            wire.Amount = 45m;
            await session.UpsertAsync<Payment>(wire);
            Assert.Equal(45m, (await session.Query<WirePayment>().FirstOrDefaultAsync((w, id) => w.Id == id, wire.Id))!.Amount);
        }
        finally
        {
            var ids = payments.Select(p => p.Id).ToArray();
            await session.Query<CardPayment>().Where((c, keys) => keys.Contains(c.Id), ids).ExecuteDeleteAsync();
            await session.Query<WirePayment>().Where((w, keys) => keys.Contains(w.Id), ids).ExecuteDeleteAsync();
        }
    }
}

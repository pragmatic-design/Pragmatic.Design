using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>Averages over a collection, declared on a DTO: an integer column's mean keeps its fraction, a decimal's reads as a decimal.</summary>
[MapFrom<Order>]
public sealed class OrderAveragesDto
{
    public Guid Id { get; set; }

    [MapProperty("Lines.Average(Quantity)")]
    public double? AverageQuantity { get; set; }

    [MapProperty("Lines.Average(UnitPrice)")]
    public decimal? AveragePrice { get; set; }
}

/// <summary>
///     <c>[MapProperty("Lines.Average(Col)")]</c> is a correlated <c>AVG(c."Col" * 1E0)</c> over an integer column: SQL Server's AVG over it would be an
///     int. Compared with LINQ over the seed; an order without lines reads NULL. The order is inserted in a transaction and rolled back.
/// </summary>
[Collection("databases")]
public class DeclaredAverageTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Averages_over_a_collection_match_linq(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var transaction = session.BeginTransaction();
        var empty = new Order { Id = Guid.NewGuid(), CustomerId = customerId, Number = "AVG-" + Guid.NewGuid().ToString("N")[..8], CreatedAt = DateTimeOffset.UtcNow };
        await session.InsertAsync(empty);

        var rows = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId, customerId).Project<OrderAveragesDto>().ToListAsync();
        var expected = seed.Orders.Where(o => o.CustomerId == customerId).ToDictionary(o => o.Id, o => seed.Lines.Where(l => l.OrderId == o.Id).ToList());
        Assert.Equal(expected.Count + 1, rows.Count);
        foreach (var row in rows)
        {
            if (row.Id == empty.Id)
            {
                Assert.Null(row.AverageQuantity);
                Assert.Null(row.AveragePrice);
                continue;
            }

            var lines = expected[row.Id];
            Assert.Equal(lines.Average(l => l.Quantity), row.AverageQuantity!.Value, 9);
            // A decimal's mean at the dialect's scale: SQL Server truncates it to 6 digits, MySQL rounds at the column's 2 plus 4.
            Assert.True(Math.Abs(lines.Average(l => l.UnitPrice) - row.AveragePrice!.Value) < 0.000001m, $"{lines.Average(l => l.UnitPrice)} vs {row.AveragePrice}");
        }

        Assert.Contains(rows, r => r.AverageQuantity % 1 != 0); // a mean with a fraction: not truncated to the integer
        transaction.Rollback();
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Average_of_an_integer_column_keeps_its_fraction(SqlDialect dialect)
    {
        if (dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null)
            return;
        var seed = databases.Seed;
        var orderId = seed.Orders.First(o => seed.Lines.Where(l => l.OrderId == o.Id).Average(l => l.Quantity) % 1 != 0).Id;
        using var session = databases.Session(dialect);

        var scalar = await session.Query<OrderLine>().Where((l, id) => l.OrderId == id, orderId).AverageAsync(l => l.Quantity);
        Assert.Equal(seed.Lines.Where(l => l.OrderId == orderId).Average(l => l.Quantity), scalar, 9);

        var grouped = await session.Query<OrderLine>().Where((l, id) => l.OrderId == id, orderId).GroupBy(l => l.OrderId).Select(g => new { Order = g.Key, Mean = g.Average(l => l.Quantity) }).ToListAsync();
        Assert.Equal(seed.Lines.Where(l => l.OrderId == orderId).Average(l => l.Quantity), Assert.Single(grouped).Mean, 9);
    }
}

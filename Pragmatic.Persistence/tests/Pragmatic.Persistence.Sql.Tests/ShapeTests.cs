using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     Client shaping over a compiled chain, as a DevExtreme grid asks it of provisus's endpoints (<c>ShapedPagedRequest</c>: a filter
///     array, sorts, skip/take, a selection, the total count; strings compared case-insensitively): the server's chain says which rows
///     and which members, the client's shape how to look at them. One command, the count included; a field outside the members is refused.
/// </summary>
[Collection("databases")]
public class ShapeTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_grid_request_over_a_projection_pages_filters_and_counts(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var seed = databases.Seed;
        string CustomerName(Order o) => seed.Customers.First(c => c.Id == o.CustomerId).Name;

        // The request as the grid sends it: filter and sort as DevExtreme's JSON, a page, the total.
        var shape = SqlShape.FromDevExtreme(
            filterJson: "[[\"Total\", \">\", 1000], \"and\", [\"CustomerName\", \"contains\", \"A\"]]",
            sortJson: "[{\"selector\": \"Total\", \"desc\": true}, {\"selector\": \"Number\", \"desc\": false}]",
            skip: 5,
            take: 10,
            requireTotalCount: true);
        var result = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(shape);

        var expected = seed.Orders.Where(o => !o.IsDeleted && o.Total > 1000m && CustomerName(o).Contains('a', StringComparison.OrdinalIgnoreCase)).OrderByDescending(o => o.Total).ThenBy(o => o.Number, StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Count, result.TotalCount);
        Assert.Equal(expected.Skip(5).Take(10).Select(o => o.Id), result.Rows.Select(r => r.Id));
        Assert.All(result.Rows, r => Assert.Equal(CustomerName(seed.Orders.First(o => o.Id == r.Id)), r.CustomerName));
        Assert.Null(result.Narrowed);
        Assert.Equal(["Id", "Number", "Total", "CustomerName"], result.Columns);

        // A narrowed selection: dictionaries of the selected members only, the rows not built.
        var narrowed = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme("[\"Total\", \">\", 1000]", "[\"Number\"]", take: 3, select: ["Id", "Number"]));
        Assert.Empty(narrowed.Rows);
        Assert.Equal(3, narrowed.Narrowed!.Count);
        Assert.All(narrowed.Narrowed, row => Assert.Equal(["Id", "Number"], row.Keys));
        Assert.Equal(seed.Orders.Where(o => !o.IsDeleted && o.Total > 1000m).OrderBy(o => o.Number, StringComparer.Ordinal).Take(3).Select(o => o.Number), narrowed.Narrowed.Select(r => (string)r["Number"]!));

        // The count alone (a count request): no rows, the total of the filtered ones; the chain's own filter still applies.
        var count = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme("[\"Total\", \"<=\", 1000]", null, take: 0, requireTotalCount: true));
        Assert.Empty(count.Rows);
        Assert.Equal(seed.Orders.Count(o => !o.IsDeleted && o.Total <= 1000m), count.TotalCount);

        // Equality, inequality, null tests, OR groups and negation, in DevExtreme's forms; a field the rows do not expose is refused.
        var or = await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme("[[\"Number\", \"ORD-100000\"], \"or\", [\"Number\", \"=\", \"ord-100001\"], \"or\", [\"!\", [\"Total\", \"<>\", null]]]", null, requireTotalCount: true));
        Assert.Equal(seed.Orders.Count(o => !o.IsDeleted && o.Number is "ORD-100000" or "ORD-100001"), or.TotalCount);
        var refused = await Assert.ThrowsAsync<SqlShapeException>(async () => await session.Query<Order>().Where(o => !o.IsDeleted).Project<OrderRowDto>().ShapeAsync(SqlShape.FromDevExtreme("[\"IsDeleted\", \"=\", true]", null)));
        Assert.Contains("'IsDeleted' is not a member of the rows", refused.Message, StringComparison.Ordinal);
        Assert.Throws<SqlShapeException>(() => SqlShape.FromDevExtreme("[\"Total\", \"between\", 1]", null));

        // The entity's rows, no projection: every column a member; a case-insensitive equality on a text column.
        var customers = await session.Query<Customer>().ShapeAsync(SqlShape.FromDevExtreme("[\"Country\", \"=\", \"it\"]", "[\"Name\"]", requireTotalCount: true));
        Assert.Equal(seed.Customers.Count(c => string.Equals(c.Country, "IT", StringComparison.OrdinalIgnoreCase)), customers.TotalCount);
        Assert.Equal(customers.TotalCount, customers.Rows.Count);
        Assert.All(customers.Rows, c => Assert.Equal("IT", c.Country, ignoreCase: true));
    }
}

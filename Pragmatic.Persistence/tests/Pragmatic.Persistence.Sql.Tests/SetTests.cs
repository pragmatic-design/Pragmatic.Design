using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     DISTINCT and the set operations: the members as a derived table the result is read from, ordered and paged after the
///     operation; the other chain's arguments travel in the first chain's slots; a Distinct before a Count counts through the
///     same derived table. Distinct also on the runtime path; the set operations are compile-time only.
/// </summary>
[Collection("databases")]
public class SetTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void Distinct_and_the_set_operations_translate_to_derived_tables_on_every_dialect()
    {
        // DISTINCT on a projection (counted through a derived table: the count's text has no ToSql, the equivalence test covers it).
        Assert.Equal("SELECT DISTINCT t.\"Country\" FROM \"Customers\" t", Session(SqlDialect.PostgreSql).Query<Customer>().Select(c => c.Country).Distinct().ToSql());
        Assert.Equal("SELECT DISTINCT t.[Country] FROM [Customers] t", Session(SqlDialect.SqlServer).Query<Customer>().Select(c => c.Country).Distinct().ToSql());

        // UNION of two filtered chains, each with its argument: the members select their columns as cN, the result reads them back, ordered and paged.
        var union = Session(SqlDialect.PostgreSql).Query<Order>()
            .Where((o, country) => o.Customer!.Country == country, "IT")
            .Union(Session(SqlDialect.PostgreSql).Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled))
            .OrderBy(o => o.Number).Take(10);
        var sql = union.ToSql();
        // No wrapper: the members alias their columns cN, the ORDER BY and the paging apply to the compound itself.
        Assert.StartsWith("SELECT t.\"Id\" AS c0, t.\"CustomerId\" AS c1", sql, StringComparison.Ordinal);
        Assert.Contains("FROM \"Orders\" t INNER JOIN \"Customers\" j_Customer ON j_Customer.\"Id\" = t.\"CustomerId\" WHERE (j_Customer.\"Country\" = @p0) UNION SELECT t.\"Id\" AS c0", sql, StringComparison.Ordinal);
        Assert.EndsWith("WHERE (t.\"Status\" = @p1) ORDER BY c2 LIMIT @p2", sql, StringComparison.Ordinal); // Number is the third column of Order
        // SQL Server pages only under an ORDER BY of result columns: without one, the wrapper with its constant ORDER BY stays.
        Assert.EndsWith(" UNION SELECT t.[Id] AS c0, t.[CustomerId] AS c1, t.[Number] AS c2, t.[Status] AS c3, t.[Total] AS c4, t.[CreatedAt] AS c5, t.[IsDeleted] AS c6 FROM [Orders] t WHERE (t.[IsDeleted] = @p0)) t ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY", Session(SqlDialect.SqlServer).Query<Order>().Union(Session(SqlDialect.SqlServer).Query<Order>().Where(o => o.IsDeleted)).Take(3).ToSql(), StringComparison.Ordinal);

        // UNION ALL, EXCEPT, INTERSECT; a Distinct after the operation on the result; SQL Server's brackets and paging.
        Assert.Contains(" UNION ALL ", Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.IsDeleted).Concat(Session(SqlDialect.Sqlite).Query<Order>().Where(o => !o.IsDeleted)).ToSql(), StringComparison.Ordinal);
        Assert.Contains(" EXCEPT ", Session(SqlDialect.Sqlite).Query<Order>().Except(Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.IsDeleted)).ToSql(), StringComparison.Ordinal);
        var intersect = Session(SqlDialect.SqlServer).Query<Order>().Select(o => o.CustomerId).Intersect(Session(SqlDialect.SqlServer).Query<Order>().Where(o => o.IsDeleted).Select(o => o.CustomerId)).Distinct().Take(5).ToSql();
        Assert.Equal("SELECT DISTINCT t.c0 FROM (SELECT t.[CustomerId] AS c0 FROM [Orders] t INTERSECT SELECT t.[CustomerId] AS c0 FROM [Orders] t WHERE (t.[IsDeleted] = @p0)) t ORDER BY (SELECT NULL) OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY", intersect);

        // A chained second operation nests the first as a wrapped member: (A UNION B) EXCEPT C, whatever the dialect's precedence; the outer level is flat.
        var chained = Session(SqlDialect.Sqlite).Query<Order>().Select(o => o.Number).Union(Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.IsDeleted).Select(o => o.Number)).Except(Session(SqlDialect.Sqlite).Query<Order>().Where(o => o.Status == OrderStatus.Open).Select(o => o.Number)).ToSql();
        Assert.Equal("SELECT t.c0 AS c0 FROM (SELECT t.\"Number\" AS c0 FROM \"Orders\" t UNION SELECT t.\"Number\" AS c0 FROM \"Orders\" t WHERE (t.\"IsDeleted\" = @p0)) t EXCEPT SELECT t.\"Number\" AS c0 FROM \"Orders\" t WHERE (t.\"Status\" = @p1)", chained);

        // The runtime path: DISTINCT, and the distinct count through the derived table.
        var session = Session(SqlDialect.PostgreSql);
        IQueryable<Customer> customers = session.Query<Customer>();
        Assert.Equal(session.Query<Customer>().Select(c => c.Country).Distinct().ToSql(), customers.Select(c => c.Country).Distinct().ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Distinct_and_the_set_operations_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var countries = seed.Customers.Select(c => c.Country).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();

        // DISTINCT values, and their count.
        Assert.Equal(countries, (await session.Query<Customer>().Select(c => c.Country).Distinct().ToListAsync()).OrderBy(c => c, StringComparer.Ordinal));
        Assert.Equal(countries.Count, await session.Query<Customer>().Select(c => c.Country).Distinct().CountAsync());
        Assert.Equal(seed.Orders.Select(o => o.CustomerId).Distinct().Count(), await session.Query<Order>().Select(o => o.CustomerId).Distinct().CountAsync());

        // UNION with arguments on both members, ordered and paged after it; the seed's answer is the set union.
        var expected = seed.Orders.Where(o => customers[o.CustomerId].Country == "IT").Union(seed.Orders.Where(o => o.Status == OrderStatus.Cancelled)).Select(o => o.Number).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var union = await session.Query<Order>()
            .Where((o, country) => o.Customer!.Country == country, "IT")
            .Union(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled))
            .OrderBy(o => o.Number).ToListAsync();
        Assert.Equal(expected, union.Select(o => o.Number));
        Assert.Equal(expected.Take(7), (await session.Query<Order>().Where((o, country) => o.Customer!.Country == country, "IT").Union(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled)).OrderBy(o => o.Number).Take(7).ToListAsync()).Select(o => o.Number));
        Assert.Equal(expected.Count, await session.Query<Order>().Where((o, country) => o.Customer!.Country == country, "IT").Union(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled)).CountAsync());

        // UNION ALL keeps the duplicates; EXCEPT and INTERSECT on projected keys.
        var italian = seed.Orders.Count(o => customers[o.CustomerId].Country == "IT");
        var cancelled = seed.Orders.Count(o => o.Status == OrderStatus.Cancelled);
        Assert.Equal(italian + cancelled, await session.Query<Order>().Where((o, country) => o.Customer!.Country == country, "IT").Concat(session.Query<Order>().Where((o, status) => o.Status == status, OrderStatus.Cancelled)).CountAsync());
        var withoutCancelled = seed.Orders.Select(o => o.CustomerId).Except(seed.Orders.Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.CustomerId)).Count();
        Assert.Equal(withoutCancelled, await session.Query<Order>().Select(o => o.CustomerId).Except(session.Query<Order>().Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.CustomerId)).CountAsync());
        var both = seed.Orders.Where(o => o.Status == OrderStatus.Open).Select(o => o.CustomerId).Intersect(seed.Orders.Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.CustomerId)).Count();
        Assert.Equal(both, await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).Select(o => o.CustomerId).Intersect(session.Query<Order>().Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.CustomerId)).CountAsync());
        Assert.Equal(both > 0, await session.Query<Order>().Where(o => o.Status == OrderStatus.Open).Select(o => o.CustomerId).Intersect(session.Query<Order>().Where(o => o.Status == OrderStatus.Cancelled).Select(o => o.CustomerId)).AnyAsync());

        // The runtime path, twice for the shape cache.
        IQueryable<Customer> composed = session.Query<Customer>();
        Assert.Equal(countries.Count, await composed.Select(c => c.Country).Distinct().CountAsync());
        Assert.Equal(countries.Count, await composed.Select(c => c.Country).Distinct().CountAsync());
        Assert.Equal(countries, (await composed.Select(c => c.Country).Distinct().ToListAsync()).OrderBy(c => c, StringComparer.Ordinal));
    }
}

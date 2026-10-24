using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Core.Tests;

/// <summary>Declared chains: a [Query] that returns the chain instead of running it; called in place, inlined by the generator where it is used.</summary>
public static partial class Chains
{
    /// <summary>The open (or any status's) orders that are not deleted: a filter reused as a root, a join's inner chain or a set operation's member.</summary>
    [Query]
    public static SqlQuery<Order> WithStatus(SqlQuery<Order> orders, OrderStatus status) => orders.Where(o => o.Status == status && !o.IsDeleted);

    [Query]
    public static SqlQuery<Customer> FromCountry(SqlQuery<Customer> customers, string country) => customers.Where(c => c.Country == country);

    /// <summary>A chain with two values and a navigation: the lines of a customer's country above a quantity.</summary>
    [Query]
    public static SqlQuery<OrderLine> HeavyLinesOf(SqlQuery<OrderLine> lines, string country, int atLeast) => lines.Where(l => l.Order!.Customer!.Country == country && l.Quantity >= atLeast);

    /// <summary>A declared query built on a declared chain: the chain's parameters become the query's.</summary>
    [Query]
    public static ValueTask<int> CountWithStatus(SqlQuery<Order> orders, OrderStatus status) => WithStatus(orders, status).CountAsync();
}

[Collection("databases")]
public class ChainTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    private static SqlSession Session(SqlDialect dialect) => new(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"), dialect);

    [Fact]
    public void A_declared_chain_is_inlined_where_it_is_called()
    {
        // As a root, with operators after it: the chain's parameter is the first slot, the Where's argument the second.
        var root = Chains.WithStatus(Session(SqlDialect.PostgreSql).Query<Order>(), OrderStatus.Open).Where((o, min) => o.Total > min, 100m).OrderBy(o => o.Number).Take(5);
        Assert.Equal("SELECT t.\"Id\", t.\"CustomerId\", t.\"Number\", t.\"Status\", t.\"Total\", t.\"CreatedAt\", t.\"IsDeleted\" FROM \"Orders\" t WHERE ((t.\"Status\" = @p0) AND (NOT (t.\"IsDeleted\" = @p1))) AND (t.\"Total\" > @p2) ORDER BY t.\"Number\" LIMIT @p3", root.ToSql());
        using var command = root.ToCommand();
        Assert.Equal([1, true, 100m, 5], Enumerable.Range(0, command.Parameters.Count).Select(i => command.Parameters[i].Value).ToArray());

        // As a join's inner chain: its filter goes into the ON clause, its value into this chain's slots.
        var joined = Session(SqlDialect.SqlServer).Query<Customer>().LeftJoin(Chains.WithStatus(Session(SqlDialect.SqlServer).Query<Order>(), OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number });
        Assert.Equal("SELECT t.[Name], j1.[Number] FROM [Customers] t LEFT JOIN [Orders] j1 ON j1.[CustomerId] = t.[Id] AND ((j1.[Status] = @p0) AND (NOT (j1.[IsDeleted] = @p1)))", joined.ToSql());

        // As the members of a set operation, each with its own value.
        var union = Chains.FromCountry(Session(SqlDialect.Sqlite).Query<Customer>(), "IT").Union(Chains.FromCountry(Session(SqlDialect.Sqlite).Query<Customer>(), "DE")).OrderBy(c => c.Name);
        Assert.Equal("SELECT t.\"Id\" AS c0, t.\"Name\" AS c1, t.\"Email\" AS c2, t.\"Country\" AS c3, t.\"CreatedAt\" AS c4 FROM \"Customers\" t WHERE (t.\"Country\" = @p0) UNION SELECT t.\"Id\" AS c0, t.\"Name\" AS c1, t.\"Email\" AS c2, t.\"Country\" AS c3, t.\"CreatedAt\" AS c4 FROM \"Customers\" t WHERE (t.\"Country\" = @p1) ORDER BY c1", union.ToSql());

        // A chain with two values and a navigation.
        var heavy = Chains.HeavyLinesOf(Session(SqlDialect.PostgreSql).Query<OrderLine>(), "IT", 8).Select(l => l.Sku);
        Assert.Equal("SELECT t.\"Sku\" FROM \"OrderLines\" t INNER JOIN \"Orders\" j_Order ON j_Order.\"Id\" = t.\"OrderId\" INNER JOIN \"Customers\" j_Order_Customer ON j_Order_Customer.\"Id\" = j_Order.\"CustomerId\" WHERE ((j_Order_Customer.\"Country\" = @p0) AND (t.\"Quantity\" >= @p1))", heavy.ToSql());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Declared_chains_compute_what_the_seed_computes(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        using var session = databases.Session(dialect);
        var customers = seed.Customers.ToDictionary(c => c.Id);
        var orders = seed.Orders.ToDictionary(o => o.Id);

        var open = seed.Orders.Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).ToList();
        Assert.Equal(open.Where(o => o.Total > 100m).OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => o.Number), (await Chains.WithStatus(session.Query<Order>(), OrderStatus.Open).Where((o, min) => o.Total > min, 100m).OrderBy(o => o.Number).Take(5).ToListAsync()).Select(o => o.Number));
        Assert.Equal(seed.Orders.Count(o => o.Status == OrderStatus.Paid && !o.IsDeleted), await Chains.WithStatus(session.Query<Order>(), OrderStatus.Paid).CountAsync());

        // The declared query built on the chain: the generated session extension.
        Assert.Equal(open.Count, await session.CountWithStatus(OrderStatus.Open));

        // As a join's inner chain: customers with their open orders, the unmatched kept.
        var leftJoined = await session.Query<Customer>().LeftJoin(Chains.WithStatus(session.Query<Order>(), OrderStatus.Open), c => c.Id, o => o.CustomerId, (c, o) => new { c.Name, Number = o.Number }).CountAsync();
        Assert.Equal(open.Count + seed.Customers.Count(c => open.All(o => o.CustomerId != c.Id)), leftJoined);

        // As set members.
        Assert.Equal(seed.Customers.Count(c => c.Country is "IT" or "DE"), await Chains.FromCountry(session.Query<Customer>(), "IT").Union(Chains.FromCountry(session.Query<Customer>(), "DE")).CountAsync());

        // Two values and a navigation, with a filter added after.
        var expected = seed.Lines.Count(l => l.Quantity >= 8 && customers[orders[l.OrderId].CustomerId].Country == "IT" && l.Sku == "BOLT");
        Assert.Equal(expected, await Chains.HeavyLinesOf(session.Query<OrderLine>(), "IT", 8).CountAsync(l => l.Sku == "BOLT"));
    }
}

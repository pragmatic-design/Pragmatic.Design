using Oracles;
using Pragmatic.Persistence.Workload;
using Xunit;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The same LINQ, written once against the translated queryable and once against EF Core on the same
///     database (<see cref="EfOracle" />), gives the same rows: SQLite always, PostgreSQL and SQL Server
///     when Docker answers.
/// </summary>
[Collection("databases")]
public class EquivalenceTests(TestDatabases databases)
{
    public static TheoryData<SqlDialect> Dialects => [SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle];

    private bool Skip(SqlDialect dialect) => dialect != SqlDialect.Sqlite && databases.DockerUnavailable is not null;

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task By_id_filtered_pages_projections_counts_and_searches_match_ef_core(SqlDialect dialect)
    {
        if (Skip(dialect))
            return; // the fixture's reason is in DockerUnavailable
        var seed = databases.Seed;
        var orderId = seed.Orders[4321].Id;
        var customerId = seed.BusyCustomer.Id;
        var text = "10042";
        var minimum = 1000m;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);

        var ours = await session.Query<Order>().FirstOrDefaultAsync((o, orderId) => o.Id == orderId, orderId);
        var theirs = await EfOracle.ById(ef, orderId);
        Assert.NotNull(ours);
        Assert.Equal(theirs!.Number, ours.Number);
        Assert.Equal(theirs.Total, ours.Total);
        Assert.Equal(theirs.Status, ours.Status);
        Assert.Equal(theirs.CreatedAt, ours.CreatedAt);
        Assert.Equal(seed.Orders[4321].CreatedAt, ours.CreatedAt);

        // The caller's values travel as the argument of the two-parameter form (the lambda parameter may shadow the local); constants stay in the lambda.
        var page = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).ToListAsync();
        var efPage = await EfOracle.OpenPage(ef, customerId);
        Assert.Equal(efPage.Select(o => o.Id), page.Select(o => o.Id));
        Assert.NotEmpty(page);

        var rows = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20)
            .Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name }).ToListAsync();
        var efRows = await EfOracle.OpenRows(ef, customerId);
        Assert.Equal(efRows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)), rows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)));

        var count = await session.Query<Order>().CountAsync((o, minimum) => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > minimum, minimum);
        Assert.Equal(await EfOracle.CountOpenAbove(ef, minimum), count);
        Assert.Equal(seed.Orders.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > minimum), count);

        var search = await session.Query<Order>().Where((o, text) => o.Number.Contains(text) && !o.IsDeleted, text).OrderBy(o => o.Number).Take(20).Select(o => new { o.Id, o.Number, Who = o.Customer!.Name }).ToListAsync();
        Assert.Equal(await EfOracle.Search(ef, text), search.Select(r => (r.Id, r.Number, r.Who)).ToList());
        Assert.Equal(10, search.Count);

        var any = await session.Query<Order>().AnyAsync(o => o.Number == "ORD-100000"); // a constant: bound from a field of the plan
        Assert.True(any);

        // A date bound as a parameter, on every dialect (SQL Server's datetimeoffset needs its scale to prepare).
        var since = seed.Orders[5000].CreatedAt;
        var later = await session.Query<Order>().CountAsync((o, since) => o.CreatedAt > since && !o.IsDeleted, since);
        Assert.Equal(seed.Orders.Count(o => o.CreatedAt > since && !o.IsDeleted), later);
        var none = await session.Query<Order>().Where(o => o.Number == "nope").AnyAsync();
        Assert.False(none);

        var countries = new[] { "IT", "DE" };
        var customers = await session.Query<Customer>().Where((c, countries) => countries.Contains(c.Country), countries).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        Assert.Equal(await EfOracle.CustomersIn(ef, countries), customers);
        Assert.Equal(26, customers.Count); // 100 customers over 8 countries in rotation: IT and DE have 13 each
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Includes_declared_projections_aggregates_dictionaries_and_rules_match_ef_core_and_the_seed(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var orderId = seed.Orders[4321].Id;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);

        // Include *→1: the navigation hydrated from the join's columns.
        var withCustomer = await session.Query<Order>().Include(o => o.Customer).FirstOrDefaultAsync((o, orderId) => o.Id == orderId, orderId);
        var efWithCustomer = await EfOracle.WithCustomer(ef, orderId);
        Assert.NotNull(withCustomer?.Customer);
        Assert.Equal(efWithCustomer!.Customer!.Name, withCustomer.Customer.Name);
        Assert.Equal(efWithCustomer.Customer.Email, withCustomer.Customer.Email);

        // Include 1→N: the lines of ten orders, loaded with one more query on the ten keys.
        var withLines = await session.Query<Order>().Include(o => o.Lines).Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).ToListAsync();
        var efWithLines = await EfOracle.WithLines(ef, customerId);
        Assert.Equal(10, withLines.Count);
        Assert.Equal(efWithLines.Select(o => (o.Id, o.Lines.Count)), withLines.Select(o => (o.Id, o.Lines.Count)));
        Assert.All(withLines, o => Assert.Equal(Seed.LinesPerOrder, o.Lines.Count));
        Assert.All(withLines, o => Assert.All(o.Lines, l => Assert.Equal(o.Id, l.OrderId)));

        // Project<TDto>: the same rows the lambda projection gives; into a record struct too (no object per row).
        var declared = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).Project<OrderRowDto>().ToListAsync();
        var efRows = await EfOracle.OpenRows(ef, customerId);
        Assert.Equal(efRows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)), declared.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)));
        var structs = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted, customerId).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).Project<OrderRowStruct>().ToListAsync();
        Assert.Equal(efRows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)), structs.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)));

        // AsAsyncEnumerable: the rows streamed from the reader, no list; the same rows as the list terminal.
        var streamed = new List<Guid>();
        await foreach (var order in session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).AsAsyncEnumerable())
            streamed.Add(order.Id);
        Assert.Equal(withLines.Select(o => o.Id), streamed);

        // Aggregates on a filtered chain.
        var open = session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId);
        var totals = await EfOracle.Totals(ef, customerId);
        Assert.Equal(totals.Sum, await open.SumAsync(o => o.Total));
        Assert.Equal(totals.Min, await open.MinAsync(o => o.Total));
        Assert.Equal(totals.Max, await open.MaxAsync(o => o.Total));
        Assert.Equal(totals.Average, await open.AverageAsync(o => o.Total), 6);
        Assert.Equal(0m, await session.Query<Order>().Where(o => o.Number == "nope").SumAsync(o => o.Total)); // SQL answers NULL on no rows: default

        // ToDictionaryAsync: keyed in memory after the list.
        var byId = await open.OrderBy(o => o.Number).Take(5).ToDictionaryAsync(o => o.Id);
        Assert.Equal(5, byId.Count);
        Assert.All(byId, pair => Assert.Equal(pair.Key, pair.Value.Id));

        // GroupBy: one statement, key and aggregates, the same groups EF Core computes.
        var perCustomer = await session.Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).ToListAsync();
        Assert.Equal(await EfOracle.PerCustomer(ef), perCustomer.Select(r => (r.Customer, r.Orders, r.Total)).OrderBy(r => r.Customer).ToList());
        Assert.Equal(100, perCustomer.Count);

        // HAVING and ORDER BY on the aggregates: the five busiest customers with more than a given number of orders.
        var atLeast = 100;
        var busiest = await session.Query<Order>().Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Where((g, atLeast) => g.Count() > atLeast, atLeast)
            .OrderByDescending(g => g.Sum(o => o.Total)).ThenBy(g => g.Key).Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).Take(5).ToListAsync();
        Assert.Equal(await EfOracle.Busiest(ef, atLeast), busiest.Select(r => (r.Customer, r.Orders, r.Total)).ToList());
        Assert.NotEmpty(busiest);
        Assert.All(busiest, r => Assert.True(r.Orders > atLeast));

        // A visibility rule on the test-only LiveOrder (the Orders table): deleted orders are not there, unless asked.
        var live = await session.Query<LiveOrder>().CountAsync();
        var every = await session.Query<LiveOrder>().WithoutFilters().CountAsync();
        Assert.Equal(seed.Orders.Count(o => !o.IsDeleted), live);
        Assert.Equal(seed.Orders.Count, every);
        var liveOfCustomer = await session.Query<LiveOrder>().Where((o, customerId) => o.CustomerId == customerId, customerId).ToListAsync();
        Assert.All(liveOfCustomer, o => Assert.False(o.IsDeleted));
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == customerId && !o.IsDeleted), liveOfCustomer.Count);
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Nested_includes_load_two_levels_like_ef_core(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var orderId = seed.Orders[4321].Id;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);

        // to-one, to-one: two joins, the customer nested in the order nested in the line.
        var lines = await session.Query<OrderLine>().Include(l => l.Order!.Customer).Where((l, orderId) => l.OrderId == orderId, orderId).OrderBy(l => l.Sku).ThenBy(l => l.Id).ToListAsync();
        var efLines = await EfOracle.LinesWithOrderAndCustomer(ef, orderId);
        Assert.Equal(Seed.LinesPerOrder, lines.Count);
        Assert.Equal(efLines.Select(l => (l.Id, l.Order!.Number, l.Order.Customer!.Name)), lines.Select(l => (l.Id, l.Order!.Number, l.Order.Customer!.Name)));

        // collection, collection: the orders with their lines, two child queries.
        var customer = await session.Query<Customer>().Include(c => c.Orders.Then(o => o.Lines)).FirstOrDefaultAsync((c, customerId) => c.Id == customerId, customerId);
        var efCustomer = await EfOracle.CustomerWithOrdersAndLines(ef, customerId);
        Assert.NotNull(customer);
        Assert.Equal(efCustomer!.Orders.Count, customer.Orders.Count);
        Assert.Equal(efCustomer.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)), customer.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)));
        Assert.All(customer.Orders, o => Assert.All(o.Lines, l => Assert.Equal(o.Id, l.OrderId)));

        // to-one, collection: the order's customer, with all the customer's orders loaded on the included object. EF Core refuses
        // this path in a no-tracking query ("Customer->Orders results in a cycle"), so the seed is the oracle here.
        var order = await session.Query<Order>().Include(o => o.Customer.Then(c => c.Orders)).FirstOrDefaultAsync((o, orderId) => o.Id == orderId, orderId);
        Assert.NotNull(order?.Customer);
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == order.CustomerId), order.Customer.Orders.Count);
        Assert.Contains(order.Customer.Orders, o => o.Id == orderId);

        // three hops: two joins, then a collection under the second included object, loaded once per distinct customer.
        var linesWithOrders = await session.Query<OrderLine>().Include(l => l.Order!.Customer.Then(c => c.Orders)).Where((l, orderId) => l.OrderId == orderId, orderId).ToListAsync();
        Assert.Equal(Seed.LinesPerOrder, linesWithOrders.Count);
        Assert.All(linesWithOrders, l => Assert.Equal(seed.Orders.Count(o => o.CustomerId == l.Order!.CustomerId), l.Order!.Customer!.Orders.Count));

        // Filtered includes: LINQ's Where inside the path filters the collection's child query (columns and constants), before a Then or alone.
        var openOnly = await session.Query<Customer>().Include(c => c.Orders.Where(o => o.Status == OrderStatus.Open).Then(o => o.Lines)).FirstOrDefaultAsync((c, customerId) => c.Id == customerId, customerId);
        var efOpenOnly = await EfOracle.CustomerWithOpenOrdersAndLines(ef, customerId);
        Assert.NotNull(openOnly);
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open), openOnly.Orders.Count);
        Assert.Equal(efOpenOnly!.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)), openOnly.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)));
        var bigLines = await session.Query<Order>().Include(o => o.Lines.Where(l => l.Quantity > 2)).Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).ToListAsync();
        var efBigLines = await EfOracle.WithBigLines(ef, customerId);
        Assert.Equal(efBigLines.Select(o => (o.Id, o.Lines.Count)), bigLines.Select(o => (o.Id, o.Lines.Count)));
        Assert.All(bigLines, o => Assert.All(o.Lines, l => Assert.True(l.Quantity > 2)));
        Assert.Contains(bigLines, o => o.Lines.Count < Seed.LinesPerOrder); // the filter left some lines out
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Declared_projections_with_collections_and_aggregates_match_ef_core(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var customerId = databases.Seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);
        static string Flat(OrderWithLinesDto o) => $"{o.Id}|{o.Number}|{o.CustomerName}|{o.LinesCount}|{o.Units}|{o.TopPrice}|{string.Join(",", o.Lines.OrderBy(l => l.Id).Select(l => $"{l.Id}:{l.Sku}:{l.Quantity}"))}";

        // Project<TDto> with a List<TChildDto> and three aggregates: one command, the lines in a companion statement, the aggregates as correlated subqueries.
        var mine = await session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).Project<OrderWithLinesDto>().ToListAsync();
        var theirs = await EfOracle.OrdersWithLines(ef, customerId);
        Assert.Equal(10, mine.Count);
        Assert.Equal(theirs.Select(Flat), mine.Select(Flat));
        Assert.All(mine, o => Assert.Equal(Seed.LinesPerOrder, o.Lines.Count));

        // Two levels of collections: the customer's orders with their lines, three statements in one command.
        var customer = await session.Query<Customer>().Where((c, customerId) => c.Id == customerId, customerId).Project<CustomerWithOrdersDto>().FirstOrDefaultAsync();
        var efCustomer = await EfOracle.CustomerWithOrders(ef, customerId);
        Assert.NotNull(customer);
        Assert.Equal((efCustomer!.Id, efCustomer.Name, efCustomer.OrdersCount), (customer.Id, customer.Name, customer.OrdersCount));
        Assert.Equal(efCustomer.Orders.OrderBy(o => o.Id).Select(Flat), customer.Orders.OrderBy(o => o.Id).Select(Flat));
        Assert.Equal(customer.OrdersCount, customer.Orders.Count);

        // The runtime path reads the registered projection, collections included: the same rows.
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10);
        var runtime = await composed.Project<Order, OrderWithLinesDto>().ToListAsync();
        Assert.Equal(mine.Select(Flat), runtime.Select(Flat));

        // Streaming cannot load a collection: refused at runtime as the typed chain is at compile time (PRAG3001).
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in composed.Project<Order, OrderWithLinesDto>().AsAsyncEnumerable())
            {
            }
        });
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task Declared_queries_give_the_rows_of_their_inline_twins(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var orderId = seed.Orders[4321].Id;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);

        var byId = await session.ById(orderId);
        Assert.Equal(seed.Orders[4321].Number, byId!.Number);

        var rows = await session.OpenRows(customerId, 2, 20);
        var efRows = await EfOracle.OpenRows(ef, customerId);
        Assert.Equal(efRows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)), rows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)));

        Assert.Equal(await EfOracle.CountOpenAbove(ef, 1000m), await session.CountOpenAbove(1000m));
        Assert.Equal((await EfOracle.Totals(ef, customerId)).Sum, await session.TotalOf(customerId));

        var countries = new[] { "IT", "DE" };
        Assert.Equal(await EfOracle.CustomersIn(ef, countries), await session.NamesIn(countries));

        var found = await session.Search("10042");
        Assert.Equal(await EfOracle.Search(ef, "10042"), found.Select(o => (o.Id, o.Number, o.Customer!.Name)).ToList());

        var five = await session.FirstFiveById(customerId);
        Assert.Equal(5, five.Count);
        Assert.All(five, pair => Assert.Equal(pair.Key, pair.Value.Id));
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_chain_composed_at_runtime_gives_the_rows_of_ef_core(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        var seed = databases.Seed;
        var customerId = seed.BusyCustomer.Id;
        using var session = databases.Session(dialect);
        using var ef = databases.Ef(dialect);

        // What a grid filter does: operators chosen at runtime on an IQueryable<T>, translated from the tree at the terminal.
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => o.CustomerId == customerId);
        var onlyOpen = true;
        if (onlyOpen)
            composed = composed.Where(o => o.Status == OrderStatus.Open);
        composed = composed.Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt);
        var count = await composed.CountAsync();
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted), count);

        var rows = await composed.Skip(20).Take(20).Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name }).ToListAsync();
        var efRows = await EfOracle.OpenRows(ef, customerId);
        Assert.Equal(efRows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)), rows.Select(r => (r.Id, r.Number, r.Total, r.CustomerName)));

        var countries = new[] { "IT", "DE" };
        IQueryable<Customer> customers = session.Query<Customer>();
        var names = await customers.Where(c => countries.Contains(c.Country)).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();
        Assert.Equal(await EfOracle.CustomersIn(ef, countries), names);

        var first = await composed.OrderBy(o => o.Number).FirstOrDefaultAsync();
        Assert.NotNull(first);
        var total = await composed.SumAsync(o => o.Total);
        Assert.Equal(seed.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).Sum(o => o.Total), total);

        IQueryable<LiveOrder> live = session.Query<LiveOrder>();
        Assert.Equal(seed.Orders.Count(o => !o.IsDeleted), await live.CountAsync()); // the rule applies on the runtime path too

        // The same shape again with other values: the second call finds the plan by the tree's shape and reads only the
        // captured leaves (a customer, a LIKE text, a page), so what it binds must be this call's values, not the first's.
        foreach (var other in new[] { seed.Customers[3].Id, seed.Customers[7].Id, customerId })
        {
            IQueryable<Order> again = session.Query<Order>();
            again = again.Where(o => o.CustomerId == other).Where(o => !o.IsDeleted);
            Assert.Equal(seed.Orders.Count(o => o.CustomerId == other && !o.IsDeleted), await again.CountAsync());
        }

        foreach (var (text, page) in new[] { ("1004", 0), ("1007", 1), ("1004", 2) })
        {
            var expected = seed.Orders.Where(o => o.Number.Contains(text, StringComparison.Ordinal) && !o.IsDeleted).OrderBy(o => o.Number, StringComparer.Ordinal).Skip(page * 5).Take(5).Select(o => o.Id).ToList();
            IQueryable<Order> search = session.Query<Order>();
            var found = await search.Where(o => o.Number.Contains(text) && !o.IsDeleted).OrderBy(o => o.Number).Skip(page * 5).Take(5).Project<Order, OrderRowDto>().ToListAsync();
            Assert.Equal(expected, found.Select(r => r.Id));
        }

        // Include on the runtime path: a to-one path hydrated from the joins, a collection loaded with a second query, through the registered maps.
        var orderId = seed.Orders[4321].Id;
        IQueryable<OrderLine> lines = session.Query<OrderLine>();
        var deepLines = await lines.Include(l => l.Order!.Customer).Where(l => l.OrderId == orderId).OrderBy(l => l.Sku).ThenBy(l => l.Id).ToListAsync();
        var efLines = await EfOracle.LinesWithOrderAndCustomer(ef, orderId);
        Assert.Equal(efLines.Select(l => (l.Id, l.Order!.Number, l.Order.Customer!.Name)), deepLines.Select(l => (l.Id, l.Order!.Number, l.Order.Customer!.Name)));
        IQueryable<Order> orders = session.Query<Order>();
        var withLines = await orders.Include(o => o.Lines).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();
        var efWithLines = await EfOracle.WithLines(ef, customerId);
        Assert.Equal(efWithLines.Select(o => (o.Id, o.Lines.Count)), withLines.Select(o => (o.Id, o.Lines.Count)));
        Assert.All(withLines, o => Assert.All(o.Lines, l => Assert.Equal(o.Id, l.OrderId)));

        // Then on the runtime path: a collection with a collection below it, a to-one with a collection below it (the seed is the oracle: EF refuses the cycle).
        var deepCustomer = await customers.Include(c => c.Orders.Then(o => o.Lines)).FirstOrDefaultAsync(c => c.Id == customerId);
        var efCustomer = await EfOracle.CustomerWithOrdersAndLines(ef, customerId);
        Assert.NotNull(deepCustomer);
        Assert.Equal(efCustomer!.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)), deepCustomer.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)));
        var ordersOfCustomer = await orders.Include(o => o.Customer.Then(c => c.Orders)).FirstOrDefaultAsync(o => o.Id == orderId);
        Assert.NotNull(ordersOfCustomer?.Customer);
        Assert.Equal(seed.Orders.Count(o => o.CustomerId == ordersOfCustomer.CustomerId), ordersOfCustomer.Customer.Orders.Count);

        // Filtered includes on the runtime path: the same child query, with the filter's constants.
        var openOnly = await customers.Include(c => c.Orders.Where(o => o.Status == OrderStatus.Open).Then(o => o.Lines)).FirstOrDefaultAsync(c => c.Id == customerId);
        var efOpenOnly = await EfOracle.CustomerWithOpenOrdersAndLines(ef, customerId);
        Assert.Equal(efOpenOnly!.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)), openOnly!.Orders.OrderBy(o => o.Id).Select(o => (o.Id, o.Lines.Count)));
        var bigLines = await orders.Include(o => o.Lines.Where(l => l.Quantity > 2)).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();
        var efBigLines = await EfOracle.WithBigLines(ef, customerId);
        Assert.Equal(efBigLines.Select(o => (o.Id, o.Lines.Count)), bigLines.Select(o => (o.Id, o.Lines.Count)));
        // A caller's value in the filter is refused: the plan is cached by shape and the child query shared.
        var minimumQuantity = 2;
        await Assert.ThrowsAsync<NotSupportedException>(() => orders.Include(o => o.Lines.Where(l => l.Quantity > minimumQuantity)).Where(o => o.CustomerId == customerId).ToListAsync().AsTask());

        // Streaming on the runtime path: the same rows, read one at a time; refused with a collection include.
        var streamed = new List<Guid>();
        await foreach (var order in orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).AsAsyncEnumerable())
            streamed.Add(order.Id);
        Assert.Equal(withLines.Select(o => o.Id), streamed);
        Assert.Throws<NotSupportedException>(() => orders.Include(o => o.Lines).AsAsyncEnumerable());
    }

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task A_branching_query_keeps_its_base_untouched(SqlDialect dialect)
    {
        if (Skip(dialect))
            return;
        using var session = databases.Session(dialect);
        var customerId = databases.Seed.BusyCustomer.Id;
        var open = session.Query<Order>().Where((o, customerId) => o.CustomerId == customerId && !o.IsDeleted, customerId);
        var total = await open.CountAsync();
        var firstPage = await open.OrderBy(o => o.Number).Take(5).ToListAsync();
        var totalAgain = await open.CountAsync();
        Assert.Equal(total, totalAgain);
        Assert.Equal(5, firstPage.Count);
        Assert.True(total > 5);
    }
}

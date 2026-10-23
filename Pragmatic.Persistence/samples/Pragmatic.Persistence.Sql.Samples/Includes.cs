using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     Loading related rows: a to-one navigation comes through the JOIN the plan already has (its columns in the same select list,
///     the object built by the generated reader); a collection is a companion statement in the same command, selecting the children
///     of the rows' keys and read from the result set that follows (one round trip whatever the depth). A declared DTO can carry
///     collections and aggregates the same way, without materialising entities.
/// </summary>
internal static class Includes
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "includes");
        var customerId = seed.BusyCustomer.Id;
        var orderId = seed.Orders[4321].Id;

        // To-one: Include(o => o.Customer) hydrates the navigation from the join.
        var withCustomer = await session.Query<Order>().Include(o => o.Customer).FirstOrDefaultAsync((o, id) => o.Id == id, orderId);
        checks.Check("include to-one", withCustomer?.Customer is not null && withCustomer.Customer.Id == withCustomer.CustomerId);

        // One-to-many: Include(o => o.Lines) adds a companion statement whose WHERE is the rows' statement selecting the keys (paging included).
        var withLines = await session.Query<Order>().Include(o => o.Lines).Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).ToListAsync();
        checks.Check("include collection", withLines.Count == 10 && withLines.All(o => o.Lines.Count == Seed.LinesPerOrder && o.Lines.All(l => l.OrderId == o.Id)));

        // Nested: a member path chains to-one joins (l => l.Order!.Customer); Then goes below a collection or a navigation, to any depth.
        var deepLines = await session.Query<OrderLine>().Include(l => l.Order!.Customer).Where((l, id) => l.OrderId == id, orderId).ToListAsync();
        var customer = await session.Query<Customer>().Include(c => c.Orders.Then(o => o.Lines)).FirstOrDefaultAsync((c, id) => c.Id == id, customerId);
        checks.Check("nested includes", deepLines.All(l => l.Order?.Customer?.Id == seed.Orders[4321].CustomerId)
            && customer is not null && customer.Orders.Count == seed.Orders.Count(o => o.CustomerId == customerId) && customer.Orders.All(o => o.Lines.Count == Seed.LinesPerOrder));

        // Filtered: LINQ's Where inside the path filters the collection (columns and constants only: the child statement is shared by every call).
        var openOnly = await session.Query<Customer>().Include(c => c.Orders.Where(o => o.Status == OrderStatus.Open).Then(o => o.Lines)).FirstOrDefaultAsync((c, id) => c.Id == id, customerId);
        checks.Check("filtered include", openOnly is not null && openOnly.Orders.Count == seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open) && openOnly.Orders.All(o => o.Lines.Count == Seed.LinesPerOrder));

        // An ordered and taken include: the children ordered as written, and at most n per parent (a window partitioned by the foreign key, in the
        // same companion statement). And explicit loading: a collection or a navigation of rows already in hand, one statement for all of them.
        var latest = await session.Query<Customer>().Include(c => c.Orders.Where(o => !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number).Take(2)).FirstOrDefaultAsync((c, id) => c.Id == id, customerId);
        var expectedLatest = seed.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).ThenBy(o => o.Number, StringComparer.Ordinal).Take(2).Select(o => o.Id).ToList();
        var inHand = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(3).ToListAsync();
        await session.LoadAsync(inHand, o => o.Lines);
        await session.LoadAsync(inHand, o => o.Customer);
        checks.Check("ordered and taken include; explicit LoadAsync", latest is not null && latest.Orders.Select(o => o.Id).SequenceEqual(expectedLatest) && inHand.All(o => o.Lines.Count == Seed.LinesPerOrder && o.Customer?.Id == customerId));

        // A DTO with a collection and aggregates: Project<OrderWithLinesDto>() reads the order's columns, its lines projected into
        // OrderLineDto in a companion statement, and LinesCount / Sum(Quantity) / Max(UnitPrice) as correlated subqueries. Two levels
        // (CustomerWithOrdersDto) are three statements in one command. The DTO reads the parent's key: the loader groups on it.
        var rows = await session.Query<Order>().Where((o, id) => o.CustomerId == id && !o.IsDeleted, customerId).OrderBy(o => o.Number).Take(10).Project<OrderWithLinesDto>().ToListAsync();
        var tree = await session.Query<Customer>().Where((c, id) => c.Id == id, customerId).Project<CustomerWithOrdersDto>().FirstOrDefaultAsync();
        checks.Check("DTO with collections and aggregates", rows.Count == 10 && rows.All(r => r.Lines.Count == Seed.LinesPerOrder && r.LinesCount == Seed.LinesPerOrder && r.Units == r.Lines.Sum(l => l.Quantity) && r.TopPrice.HasValue)
            && tree is not null && tree.OrdersCount == tree.Orders.Count && tree.Orders.All(o => o.Lines.Count == Seed.LinesPerOrder));
        return checks.Failures;
    }
}

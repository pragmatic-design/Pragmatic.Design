using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     The runtime path: a chain typed as IQueryable&lt;T&gt; binds to System.Linq's operators and builds an expression tree (a grid filter
///     choosing operators from options, a query assembled from conditions). The async terminals translate the tree with the generator's
///     rules through the maps the generator registered; the translation is cached by the tree's shape, so the second call with the same
///     shape only reads the captured values. Nothing is compiled from the tree: AOT keeps working. Project&lt;T, TDto&gt;() reads the
///     registered projection (a Select with an object initializer would be a tree that is not trim-safe).
/// </summary>
internal static class Runtime
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "runtime");
        var customerId = seed.BusyCustomer.Id;

        // A chain composed from conditions the code does not know in advance: each Where is appended as the options say.
        IQueryable<Order> composed = session.Query<Order>();
        composed = composed.Where(o => o.CustomerId == customerId);
        var onlyOpen = true;
        if (onlyOpen)
            composed = composed.Where(o => o.Status == OrderStatus.Open && !o.IsDeleted);
        composed = composed.OrderByDescending(o => o.CreatedAt).Skip(20).Take(20);
        var rows = await composed.Project<Order, OrderRowDto>().ToListAsync();
        var count = await composed.CountAsync();
        var expected = seed.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).Select(o => o.Id);
        checks.Check("composed chain, declared projection", rows.Select(r => r.Id).SequenceEqual(expected) && count == seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted));

        // The same SQL as the compiled twin (the same two Where steps on the typed chain): the two paths share the rules.
        var compiled = session.Query<Order>().Where((o, id) => o.CustomerId == id, customerId).Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).Project<OrderRowDto>();
        checks.Check("same SQL as the compiled chain", compiled.ToSql() == composed.Project<Order, OrderRowDto>().ToSql());

        // The shape cache: the same shape with other values translates once; each call binds its own leaves.
        var hits = true;
        foreach (var other in new[] { seed.Customers[3].Id, seed.Customers[7].Id, customerId })
        {
            IQueryable<Order> again = session.Query<Order>();
            hits &= await again.Where(o => o.CustomerId == other && !o.IsDeleted).CountAsync() == seed.Orders.Count(o => o.CustomerId == other && !o.IsDeleted);
        }

        checks.Check("shape cache", hits);

        // Include, keyset and a DTO with collections on the runtime path: the same statements as the typed chain.
        IQueryable<Customer> customers = session.Query<Customer>();
        var deep = await customers.Include(c => c.Orders.Where(o => o.Status == OrderStatus.Open).Then(o => o.Lines)).FirstOrDefaultAsync(c => c.Id == customerId);
        IQueryable<Order> ordered = session.Query<Order>();
        ordered = ordered.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(20).ToListAsync();
        var next = await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(20).ToListAsync();
        var byOffset = await ordered.Skip(20).Take(20).ToListAsync();
        var withLines = await ordered.Take(10).Project<Order, OrderWithLinesDto>().ToListAsync();
        checks.Check("runtime include, cursor, DTO with collections", deep is not null && deep.Orders.Count == seed.Orders.Count(o => o.CustomerId == customerId && o.Status == OrderStatus.Open) && deep.Orders.All(o => o.Lines.Count == Seed.LinesPerOrder)
            && next.Select(o => o.Id).SequenceEqual(byOffset.Select(o => o.Id)) && withLines.Count == 10 && withLines.All(r => r.Lines.Count == Seed.LinesPerOrder && r.LinesCount == Seed.LinesPerOrder));
        return checks.Failures;
    }
}

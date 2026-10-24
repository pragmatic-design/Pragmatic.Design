using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;

namespace PragmaticDocumentSamples.Orders;

/// <summary>
///     Every feature of the document module once, the same code on both backends: the chains name the customer (the orders' partition key),
///     so on Cosmos DB they run in one partition, where every form is served. Each sample says what it shows and checks it against the seed.
/// </summary>
internal static class OrderSamples
{
    public static async Task<SampleRun> RunAllAsync(string backend, DocSession session, Seed seed, List<OrderDocument> orders, List<CustomerDocument> customers)
    {
        var checks = new Checks(backend);
        var customer = seed.BusyCustomer.Id;
        var mine = orders.Where(o => o.CustomerId == customer).ToList();

        // A filtered, ordered page: the predicate's value in the two-parameter form (no capture), Skip and Take as the query's own.
        var page = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id && o.Status == OrderStatus.Open && !o.IsDeleted, customer)
            .OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).ToListAsync();
        checks.Check("a filtered, ordered page, read whole (lines, an array of values, an embedded address)",
            page.Select(o => o.Id).SequenceEqual(mine.Where(o => o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(5).Take(10).Select(o => o.Id))
            && page.All(o => o.Lines.Count == Seed.LinesPerOrder && o.Skus.Count > 0));

        // Scalars: a count, an existence, the first by key (a tuple as the argument), the largest, the smallest, the sum.
        var count = await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && !o.IsDeleted, customer);
        var any = await session.Query<OrderDocument>().AnyAsync((o, id) => o.CustomerId == id, customer);
        var some = mine[3];
        var byKey = await session.Query<OrderDocument>().FirstOrDefaultAsync((o, k) => o.CustomerId == k.Customer && o.Id == k.Id, (Customer: customer, some.Id));
        var theirs = session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer);
        var max = await theirs.MaxAsync(o => o.Total);
        var min = await theirs.MinAsync(o => o.CreatedAt);
        var sum = await theirs.SumAsync(o => o.Total);
        checks.Check("count, any, first by key, max, min, sum",
            count == mine.Count(o => !o.IsDeleted) && any && byKey?.Number == some.Number && max == mine.Max(o => o.Total) && min == mine.Min(o => o.CreatedAt)
            && Math.Round(sum, 2) == mine.Sum(o => o.Total)); // Cosmos DB sums doubles: exact to the cent here

        // A projection: the Select runs in .NET on documents read with only the fields it names.
        var rows = await theirs.OrderBy(o => o.Number).Take(5).Select(o => new { o.Number, Lines = o.Lines.Count }).ToListAsync();
        checks.Check("a projection (anonymous), the document read with the fields it names",
            rows.Select(r => r.Number).SequenceEqual(mine.OrderBy(o => o.Number, StringComparer.Ordinal).Take(5).Select(o => o.Number)) && rows.All(r => r.Lines == Seed.LinesPerOrder));

        // A list of values as $in / ARRAY_CONTAINS, a text match (the text as it is: no regex of the caller's).
        var statuses = new List<OrderStatus> { OrderStatus.Paid, OrderStatus.Cancelled };
        var closed = await session.Query<OrderDocument>().CountAsync((o, k) => o.CustomerId == k.Customer && k.Statuses.Contains(o.Status), (Customer: customer, Statuses: statuses));
        var text = mine[0].Number.Substring(4, 2);
        var matching = await session.Query<OrderDocument>().CountAsync((o, k) => o.CustomerId == k.Customer && o.Number.Contains(k.Text), (Customer: customer, Text: text));
        checks.Check("a list of values (an enum's), a text match",
            closed == mine.Count(o => statuses.Contains(o.Status)) && matching == mine.Count(o => o.Number.Contains(text, StringComparison.Ordinal)));

        // Keyset pagination: the page after the last document's (CreatedAt, Id) is the page OFFSET gives.
        var ordered = theirs.OrderBy(o => o.CreatedAt).ThenBy(o => o.Id);
        var first = await ordered.Take(7).ToListAsync();
        var after = await ordered.After((first[^1].CreatedAt, first[^1].Id)).Take(7).ToListAsync();
        var byOffset = await ordered.Skip(7).Take(7).ToListAsync();
        checks.Check("keyset pagination", after.Count == 7 && after.Select(o => o.Id).SequenceEqual(byOffset.Select(o => o.Id)));

        // Inside the document: a condition on an embedded array's elements, on an array of values, on an embedded document's field and its absence.
        var heavy = await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.Lines.Any(l => l.Quantity > 5 && l.UnitPrice > 100m), customer);
        var bolts = await session.Query<OrderDocument>().CountAsync((o, k) => o.CustomerId == k.Customer && o.Skus.Any(s => s == k.Sku), (Customer: customer, Sku: "BOLT"));
        var unshipped = await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.ShipTo == null, customer);
        var high = await session.Query<OrderDocument>().CountAsync((o, id) => o.CustomerId == id && o.ShipTo!.Floor > 3, customer);
        checks.Check("embedded arrays and documents queried in place",
            heavy == mine.Count(o => o.Lines.Any(l => l.Quantity > 5 && l.UnitPrice > 100m)) && bolts == mine.Count(o => o.Skus.Contains("BOLT"))
            && unshipped == mine.Count(o => o.ShipTo is null) && high == mine.Count(o => o.ShipTo?.Floor > 3));

        // Customers: another collection (partitioned by its key), a predicate on a string field.
        var italians = await session.Query<CustomerDocument>().Where(c => c.Country == "IT").ToListAsync();
        checks.Check("another collection, read across its partitions (a filter alone)", italians.Select(c => c.Id).Order().SequenceEqual(customers.Where(c => c.Country == "IT").Select(c => c.Id).Order()));

        checks.Add(await OrderWrites.RunAsync(backend, session));
        return checks.Run;
    }

    /// <summary>
    ///     Cosmos DB only: an ordered page across partitions, which the gateway does not serve, read range by range and merged, against the
    ///     same documents read unordered and sorted here.
    /// </summary>
    public static async Task<SampleRun> CosmosLimitsAsync(DocSession session)
    {
        var checks = new Checks("Cosmos");
        var page = await session.Query<OrderDocument>().OrderBy(o => o.Number).Skip(2).Take(5).ToListAsync();
        var all = await session.Query<OrderDocument>().ToListAsync(); // a filter alone: served across partitions
        var expected = all.Select(o => o.Number).Order(StringComparer.Ordinal).Skip(2).Take(5);
        checks.Check("an ordered page across partitions, merged range by range", page.Select(o => o.Number).SequenceEqual(expected));
        return checks.Run;
    }
}

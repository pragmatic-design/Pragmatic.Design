using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;

namespace PragmaticDocumentSamples.Orders;

/// <summary>
///     Across a document's lists and across collections, on both backends: a SelectMany of the embedded lines (MongoDB <c>$unwind</c>, Cosmos DB
///     <c>JOIN u IN c.Lines</c>), a Join of the orders with their customers (MongoDB <c>$lookup</c>, Cosmos DB a second query joined here); on
///     MongoDB a Where over the groups of a GroupBy (HAVING).
/// </summary>
internal static class Joins
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession session, Seed seed, List<OrderDocument> orders, List<CustomerDocument> customers)
    {
        var checks = new Checks(backend);
        var customer = seed.BusyCustomer.Id;
        var mine = orders.Where(o => o.CustomerId == customer).OrderBy(o => o.Number, StringComparer.Ordinal).ToList();

        var lines = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).OrderBy(o => o.Number).SelectMany(o => o.Lines).Take(10).ToListAsync();
        checks.Check("the embedded lines of the orders, flattened (SelectMany)", lines.Select(l => l.Id).SequenceEqual(mine.SelectMany(o => o.Lines).Take(10).Select(l => l.Id)));

        var country = customers.First(c => c.Id == customer).Country;
        var joined = await session.Query<OrderDocument>().Where((o, id) => o.CustomerId == id, customer).OrderBy(o => o.Number)
            .Join(session.Query<CustomerDocument>().Where((c, k) => c.Country == k, country), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name }).Take(5).ToListAsync();
        var name = customers.First(c => c.Id == customer).Name;
        checks.Check("the orders joined with their customer (another collection)", joined.Select(j => (j.Number, j.Name)).SequenceEqual(mine.Take(5).Select(o => (o.Number, name))));

        if (session.Mongo is not null)
        {
            var busy = await session.Query<OrderDocument>().GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count() }).Where((g, least) => g.Count >= least, 100).OrderByDescending(g => g.Count).ToListAsync();
            var expected = orders.GroupBy(o => o.Status).Select(g => (g.Key, g.Count())).Where(g => g.Item2 >= 100).OrderByDescending(g => g.Item2).ToList();
            checks.Check("a Where and an ordering over the groups (HAVING)", expected.Count > 0 && busy.Select(g => (g.Status, g.Count)).SequenceEqual(expected));
        }

        return checks.Run;
    }
}

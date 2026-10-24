using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Workload;
using Pragmatic.Persistence.Workload;

namespace PragmaticDocumentSamples.Orders;

/// <summary>
///     MongoDB only: a GroupBy into a new object (one <c>$group</c>), and an update and a delete by filter (one command each). Cosmos DB refuses
///     both forms (no GROUP BY translated, writes by id only); the transactions need a replica set, which the samples' server is not.
/// </summary>
internal static class Pipelines
{
    public static async Task<SampleRun> RunAsync(string backend, DocSession session, List<OrderDocument> orders)
    {
        var checks = new Checks(backend);
        var byStatus = await session.Query<OrderDocument>().Where(o => !o.IsDeleted).GroupBy(o => o.Status).Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(o => o.Total) }).ToListAsync();
        var expected = orders.Where(o => !o.IsDeleted).GroupBy(o => o.Status).Select(g => (g.Key, g.Count(), g.Sum(o => o.Total))).OrderBy(g => g.Key);
        checks.Check("a group by into a new object (key, count, sum)", byStatus.Select(g => (g.Status, g.Count, g.Total)).OrderBy(g => g.Status).SequenceEqual(expected));

        var customer = Guid.NewGuid();
        await session.InsertManyAsync(Enumerable.Range(1, 3).Select(i => new OrderDocument { Id = Guid.NewGuid(), CustomerId = customer, Number = $"ORD-CMD{i}", Status = OrderStatus.Open, Total = i, CreatedAt = new DateTimeOffset(2026, 10, 20, 12, i, 0, TimeSpan.Zero) }).ToList());
        var paid = await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c, customer).ExecuteUpdateAsync((s, status) => s.Set(o => o.Status, status), OrderStatus.Paid);
        var deleted = await session.Query<OrderDocument>().Where((o, c) => o.CustomerId == c && o.Status == OrderStatus.Paid, customer).ExecuteDeleteAsync();
        checks.Check("an update and a delete by filter, one command each", paid == 3 && deleted == 3 && !await session.Query<OrderDocument>().AnyAsync((o, c) => o.CustomerId == c, customer));
        return checks.Run;
    }
}

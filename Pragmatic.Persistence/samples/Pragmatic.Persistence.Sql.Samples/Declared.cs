using Pragmatic.Persistence.Workload;
using Pragmatic.Persistence.Sql.Attributes;
using Pragmatic.Persistence.Sql.Writing;

namespace Pragmatic.Persistence.Sql.Samples;

/// <summary>
///     The declared form: a [Query] is a static method of a static partial class, taking a SqlQuery&lt;T&gt; first and returning one chain
///     on it. The generator reads the chain once and emits an extension on the session with the same name and the remaining parameters,
///     whose body binds the values straight into the prepared command: no tree, no slot bookkeeping at the call site, the floor of hand
///     written ADO. The definition itself never runs. Each [Query] also gets an overload on a SqlBatch: several in one command.
/// </summary>
public static partial class SampleQueries
{
    [Query]
    private static ValueTask<Order?> ById(SqlQuery<Order> orders, Guid id) => orders.FirstOrDefaultAsync(o => o.Id == id);

    [Query]
    private static ValueTask<List<OrderRowDto>> OpenRows(SqlQuery<Order> orders, Guid customerId, int page, int pageSize)
        => orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Project<OrderRowDto>().ToListAsync();

    [Query]
    private static ValueTask<int> CountOpenAbove(SqlQuery<Order> orders, decimal minimum)
        => orders.CountAsync(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > minimum);

    [Query]
    private static ValueTask<decimal> TotalOf(SqlQuery<Order> orders, Guid customerId)
        => orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).SumAsync(o => o.Total);

    /// <summary>A collection include in the declared form: its companion statement travels in the same command, in a batch too.</summary>
    [Query]
    private static ValueTask<List<Order>> WithLines(SqlQuery<Order> orders, Guid customerId)
        => orders.Include(o => o.Lines).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();

    /// <summary>Keyset pagination in the declared form: the cursor is a parameter like any other.</summary>
    [Query]
    private static ValueTask<List<Order>> PageAfter(SqlQuery<Order> orders, (DateTimeOffset CreatedAt, Guid Id) cursor, int pageSize)
        => orders.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).After(cursor).Take(pageSize).ToListAsync();

    /// <summary>The page before a cursor in the declared form: the previous page, in the chain's order.</summary>
    [Query]
    private static ValueTask<List<Order>> PageBefore(SqlQuery<Order> orders, (DateTimeOffset CreatedAt, Guid Id) cursor, int pageSize)
        => orders.Where(o => !o.IsDeleted).OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Before(cursor).Take(pageSize).ToListAsync();

    /// <summary>A declared update: the SET value is a parameter bound from the method's argument, the version advances in SQL.</summary>
    [Query]
    private static ValueTask<int> Rekind(SqlQuery<Event> events, Guid orderId, string from, string to)
        => events.Where(e => e.OrderId == orderId && e.Kind == from).ExecuteUpdateAsync(s => s.SetProperty(e => e.Kind, to).SetProperty(e => e.Version, e => e.Version + 1));
}

internal static class Declared
{
    public static async Task<int> RunAsync(SqlSession session, Seed seed)
    {
        var checks = new Checks(session, "declared");
        var customerId = seed.BusyCustomer.Id;
        var orderId = seed.Orders[4321].Id;

        // The generated extensions: session.ById(id), session.OpenRows(customerId, page, pageSize)… the same rows as the inline chains.
        var byId = await session.ById(orderId);
        var rows = await session.OpenRows(customerId, 2, 20);
        var expectedRows = seed.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).Select(o => o.Id);
        checks.Check("declared queries", byId?.Number == seed.Orders[4321].Number && rows.Select(r => r.Id).SequenceEqual(expectedRows) && await session.CountOpenAbove(1000m) == seed.Orders.Count(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > 1000m));

        // A declared keyset page and a declared update.
        var first = await session.PageAfter((DateTimeOffset.MinValue, Guid.Empty), 20);
        var second = await session.PageAfter((first[^1].CreatedAt, first[^1].Id), 20);
        var back = await session.PageBefore((second[0].CreatedAt, second[0].Id), 20);
        var eventOrder = Guid.NewGuid();
        await session.InsertManyAsync(Enumerable.Range(0, 3).Select(i => new Event { Id = Guid.NewGuid(), OrderId = eventOrder, Kind = "created", At = DateTimeOffset.UnixEpoch }).ToList());
        var rekinded = await session.Rekind(eventOrder, "created", "paid");
        checks.Check("declared cursor and update", first.Count == 20 && second.Count == 20 && !first.Select(o => o.Id).Intersect(second.Select(o => o.Id)).Any() && back.Select(o => o.Id).SequenceEqual(first.Select(o => o.Id)) && rekinded == 3);

        // A batch: the same declared queries recorded on session.Batch(), run in one command (one round trip), each answered by its typed item.
        var batch = session.Batch();
        var batchedById = batch.ById(orderId);
        var batchedRows = batch.OpenRows(customerId, 2, 20);
        var batchedCount = batch.CountOpenAbove(1000m);
        var batchedTotal = batch.TotalOf(customerId);
        var batchedLines = batch.WithLines(customerId);
        await batch.ExecuteAsync();
        checks.Check("batch of declared queries", batchedById.Result?.Id == orderId && batchedRows.Result.Select(r => r.Id).SequenceEqual(rows.Select(r => r.Id)) && batchedCount.Result == await session.CountOpenAbove(1000m)
            && batchedTotal.Result == await session.TotalOf(customerId) && batchedLines.Result.Count == 10 && batchedLines.Result.All(o => o.Lines.Count == Seed.LinesPerOrder));
        return checks.Failures;
    }
}

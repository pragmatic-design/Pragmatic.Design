using Pragmatic.Persistence.Workload;

namespace Pragmatic.Persistence.Sql.Tests;

/// <summary>
///     The declared form: each [Query] definition is read once by the generator, which emits the extension
///     on SqlSession of the same name (<c>session.OpenRows(customerId, 2, 20)</c>) with the parameters' values
///     bound straight into the prepared command. The definitions themselves are never executed.
/// </summary>
public static partial class OrderQueries
{
    [Query]
    private static ValueTask<Order?> ById(SqlQuery<Order> orders, Guid id) => orders.FirstOrDefaultAsync(o => o.Id == id);

    [Query]
    private static ValueTask<List<OrderRowDto>> OpenRows(SqlQuery<Order> orders, Guid customerId, int page, int pageSize)
        => orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).Project<OrderRowDto>().ToListAsync();

    [Query]
    private static ValueTask<int> CountOpenAbove(SqlQuery<Order> orders, decimal minimum)
        => orders.CountAsync(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > minimum);

    /// <summary>A list in a declared query: an array parameter on PostgreSQL (so the query joins a batch there), an expansion per call elsewhere.</summary>
    [Query]
    private static ValueTask<int> CountInIds(SqlQuery<Order> orders, List<Guid> ids)
        => orders.CountAsync(o => ids.Contains(o.Id));

    [Query]
    private static ValueTask<decimal> TotalOf(SqlQuery<Order> orders, Guid customerId)
        => orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).SumAsync(o => o.Total);

    [Query]
    private static ValueTask<List<string>> NamesIn(SqlQuery<Customer> customers, string[] countries)
        => customers.Where(c => countries.Contains(c.Country)).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();

    [Query]
    private static ValueTask<List<Order>> Search(SqlQuery<Order> orders, string text)
        => orders.Include(o => o.Customer).Where(o => o.Number.Contains(text) && !o.IsDeleted).OrderBy(o => o.Number).Take(20).ToListAsync();

    /// <summary>A collection include in the declared form: its companion statement travels in the same command, in a batch too.</summary>
    [Query]
    private static ValueTask<List<Order>> WithLines(SqlQuery<Order> orders, Guid customerId)
        => orders.Include(o => o.Lines).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();

    [Query]
    private static ValueTask<Dictionary<Guid, Order>> FirstFiveById(SqlQuery<Order> orders, Guid customerId)
        => orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(5).ToDictionaryAsync(o => o.Id);

    /// <summary>A declared update: the SET values are parameters bound from the method's arguments, the version advances in SQL.</summary>
    [Query]
    private static ValueTask<int> Rekind(SqlQuery<Event> events, Guid orderId, string from, string to)
        => events.Where(e => e.OrderId == orderId && e.Kind == from).ExecuteUpdateAsync(s => s.SetProperty(e => e.Kind, to).SetProperty(e => e.Version, e => e.Version + 1));

    [Query]
    private static ValueTask<int> PurgeEvents(SqlQuery<Event> events, Guid orderId) => events.Where(e => e.OrderId == orderId).ExecuteDeleteAsync();
}

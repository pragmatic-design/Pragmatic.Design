using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Sql.Core.Tests;
using Pragmatic.Persistence.Workload;

// The same LINQ through EF Core, in a namespace outside Pragmatic.Persistence.Sql: there the runtime's
// terminal operators (ToListAsync, CountAsync, …) would win the lookup over EF's by namespace nesting, and
// the test would be asking the spike to run EF's queryable. Here only EF's extension methods are in scope.
namespace Oracles;

public static class EfOracle
{
    public static Task<Order?> ById(WorkloadDbContext ef, Guid orderId) => ef.Orders.FirstOrDefaultAsync(o => o.Id == orderId);

    public static Task<List<Order>> OpenPage(WorkloadDbContext ef, Guid customerId)
        => ef.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20).ToListAsync();

    public static Task<List<OrderRowDto>> OpenRows(WorkloadDbContext ef, Guid customerId)
        => ef.Orders.Where(o => o.CustomerId == customerId && o.Status == OrderStatus.Open && !o.IsDeleted).OrderByDescending(o => o.CreatedAt).Skip(20).Take(20)
            .Select(o => new OrderRowDto { Id = o.Id, Number = o.Number, Total = o.Total, CustomerName = o.Customer!.Name }).ToListAsync();

    public static Task<int> CountOpenAbove(WorkloadDbContext ef, decimal minimum)
        => ef.Orders.CountAsync(o => o.Status == OrderStatus.Open && !o.IsDeleted && o.Total > minimum);

    public static Task<List<(Guid Id, string Number, string Who)>> Search(WorkloadDbContext ef, string text)
        => ef.Orders.Where(o => o.Number.Contains(text) && !o.IsDeleted).OrderBy(o => o.Number).Take(20).Select(o => new { o.Id, o.Number, Who = o.Customer!.Name }).ToListAsync()
            .ContinueWith(t => t.Result.Select(r => (r.Id, r.Number, r.Who)).ToList(), TaskScheduler.Default);

    public static Task<List<string>> CustomersIn(WorkloadDbContext ef, string[] countries)
        => ef.Customers.Where(c => countries.Contains(c.Country)).OrderBy(c => c.Name).Select(c => c.Name).ToListAsync();

    public static Task<Order?> WithCustomer(WorkloadDbContext ef, Guid orderId) => ef.Orders.Include(o => o.Customer).FirstOrDefaultAsync(o => o.Id == orderId);

    // provisus' association-task visibility filter, EF Core's form: type tests and casts on a TPH navigation, the conditional as the predicate itself.
    public static Task<List<Guid>> VisibleReminders(WorkloadDbContext ef, Guid[] ids)
        => ef.Reminders.Where(r =>
            r.Assignment is CustomerAssignment
                ? ((CustomerAssignment)r.Assignment).Parent.Orders.Count == 0 || ((CustomerAssignment)r.Assignment).Parent.Orders.All(o => !o.IsDeleted && ids.Contains(o.Id))
            : r.Assignment is OrderAssignment
                ? ids.Contains(((OrderAssignment)r.Assignment).Parent.CustomerId)
            : r.Assignment.ParentType == ParentKind.Site
                ? ids.Contains(r.Assignment.ParentId)
            : true).Select(r => r.Id).ToListAsync();

    public static Task<List<Order>> WithLines(WorkloadDbContext ef, Guid customerId)
        => ef.Orders.Include(o => o.Lines).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();

    // A DTO with a nested collection and aggregates, EF Core's form: a Select with a nested Select and the aggregates on the navigation.
    public static Task<List<OrderWithLinesDto>> OrdersWithLines(WorkloadDbContext ef, Guid customerId)
        => ef.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).Select(o => new OrderWithLinesDto
        {
            Id = o.Id,
            Number = o.Number,
            CustomerName = o.Customer!.Name,
            LinesCount = o.Lines.Count,
            Units = o.Lines.Sum(l => l.Quantity),
            TopPrice = o.Lines.Max(l => (decimal?)l.UnitPrice),
            Lines = o.Lines.Select(l => new OrderLineDto { Id = l.Id, Sku = l.Sku, Quantity = l.Quantity }).ToList(),
        }).ToListAsync();

    public static Task<CustomerWithOrdersDto?> CustomerWithOrders(WorkloadDbContext ef, Guid customerId)
        => ef.Customers.Where(c => c.Id == customerId).Select(c => new CustomerWithOrdersDto
        {
            Id = c.Id,
            Name = c.Name,
            OrdersCount = c.Orders.Count,
            Orders = c.Orders.Select(o => new OrderWithLinesDto
            {
                Id = o.Id,
                Number = o.Number,
                CustomerName = o.Customer!.Name,
                LinesCount = o.Lines.Count,
                Units = o.Lines.Sum(l => l.Quantity),
                TopPrice = o.Lines.Max(l => (decimal?)l.UnitPrice),
                Lines = o.Lines.Select(l => new OrderLineDto { Id = l.Id, Sku = l.Sku, Quantity = l.Quantity }).ToList(),
            }).ToList(),
        }).FirstOrDefaultAsync();

    public static Task<List<OrderLine>> LinesWithOrderAndCustomer(WorkloadDbContext ef, Guid orderId)
        => ef.OrderLines.Include(l => l.Order).ThenInclude(o => o!.Customer).Where(l => l.OrderId == orderId).OrderBy(l => l.Sku).ThenBy(l => l.Id).ToListAsync();

    public static Task<Customer?> CustomerWithOrdersAndLines(WorkloadDbContext ef, Guid customerId)
        => ef.Customers.Include(c => c.Orders).ThenInclude(o => o.Lines).FirstOrDefaultAsync(c => c.Id == customerId);

    // Filtered includes, EF Core's form: the predicate inside the Include lambda.
    public static Task<Customer?> CustomerWithOpenOrdersAndLines(WorkloadDbContext ef, Guid customerId)
        => ef.Customers.Include(c => c.Orders.Where(o => o.Status == OrderStatus.Open)).ThenInclude(o => o.Lines).FirstOrDefaultAsync(c => c.Id == customerId);

    public static Task<List<Order>> WithBigLines(WorkloadDbContext ef, Guid customerId)
        => ef.Orders.Include(o => o.Lines.Where(l => l.Quantity > 2)).Where(o => o.CustomerId == customerId && !o.IsDeleted).OrderBy(o => o.Number).Take(10).ToListAsync();

    public static Task<List<(Guid Customer, int Orders, decimal Total)>> Busiest(WorkloadDbContext ef, int atLeast)
        => ef.Orders.Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Where(g => g.Count() > atLeast).OrderByDescending(g => g.Sum(o => o.Total)).ThenBy(g => g.Key)
            .Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).Take(5).ToListAsync()
            .ContinueWith(t => t.Result.Select(r => (r.Customer, r.Orders, r.Total)).ToList(), TaskScheduler.Default);

    public static Task<List<(Guid Customer, int Orders, decimal Total)>> PerCustomer(WorkloadDbContext ef)
        => ef.Orders.Where(o => !o.IsDeleted).GroupBy(o => o.CustomerId).Select(g => new { Customer = g.Key, Orders = g.Count(), Total = g.Sum(o => o.Total) }).ToListAsync()
            .ContinueWith(t => t.Result.Select(r => (r.Customer, r.Orders, r.Total)).OrderBy(r => r.Customer).ToList(), TaskScheduler.Default);

    public static async Task<(decimal Sum, decimal Min, decimal Max, double Average)> Totals(WorkloadDbContext ef, Guid customerId)
    {
        var open = ef.Orders.Where(o => o.CustomerId == customerId && !o.IsDeleted);
        return (await open.SumAsync(o => o.Total), await open.MinAsync(o => o.Total), await open.MaxAsync(o => o.Total), (double)await open.AverageAsync(o => o.Total));
    }
}

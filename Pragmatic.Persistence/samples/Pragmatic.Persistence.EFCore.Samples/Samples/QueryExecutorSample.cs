using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates EfCoreQueryExecutor with paged, filtered, and non-paged queries.
///     Shows IPagedQuery, IQuery, and the PagedResult response type.
/// </summary>
public static class QueryExecutorSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 6. Query Executor ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"QueryDb_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);

        // Seed orders
        var customer = new Customer
        {
            PersistenceId = Guid.CreateVersion7(),
            Name = "ACME Corp",
            Email = "acme@example.com"
        };
        db.Customers.Add(customer);

        var orders = new[]
        {
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-001", Status = OrderStatus.Pending, Total = 150m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-002", Status = OrderStatus.Processing, Total = 320m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-003", Status = OrderStatus.Shipped, Total = 75m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-004", Status = OrderStatus.Delivered, Total = 500m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-005", Status = OrderStatus.Pending, Total = 200m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-006", Status = OrderStatus.Processing, Total = 450m, Customer = customer },
            new Order { PersistenceId = Guid.CreateVersion7(), OrderNumber = "ORD-007", Status = OrderStatus.Cancelled, Total = 100m, Customer = customer },
        };
        db.Orders.AddRange(orders);
        await db.SaveChangesAsync();

        var executor = new EfCoreQueryExecutor();

        // ── Paged query ──
        Console.WriteLine("  Paged query: page 1, pageSize 3:");
        var pagedResult = await executor.ExecuteAsync(
            new OrdersPagedQuery { Page = 1, PageSize = 3 },
            db.Orders);

        Console.WriteLine($"    Page {pagedResult.Page}/{pagedResult.TotalPages} — {pagedResult.Items.Count} items (of {pagedResult.TotalCount} total)");
        foreach (var o in pagedResult.Items)
            Console.WriteLine($"    [{o.OrderNumber}] {o.Status,-12} {o.Total,10:C}");
        Console.WriteLine();

        // ── Filtered query ──
        Console.WriteLine("  Filtered query: Total >= 200:");
        var filteredResult = await executor.ExecuteAsync(
            new HighValueOrdersQuery { MinTotal = 200m, Page = 1, PageSize = 10 },
            db.Orders);

        Console.WriteLine($"    Found {filteredResult.TotalCount} orders:");
        foreach (var o in filteredResult.Items)
            Console.WriteLine($"    [{o.OrderNumber}] {o.Status,-12} {o.Total,10:C}");
        Console.WriteLine();

        // ── Non-paged query ──
        Console.WriteLine("  Non-paged query: all pending orders:");
        var pendingOrders = await executor.ExecuteAllAsync(
            new PendingOrdersQuery(),
            db.Orders);

        Console.WriteLine($"    Found {pendingOrders.Count} pending orders:");
        foreach (var o in pendingOrders)
            Console.WriteLine($"    [{o.OrderNumber}] {o.Total,10:C}");
        Console.WriteLine();
    }
}

// ── Query implementations ──

file sealed class OrdersPagedQuery : IPagedQuery<Order>
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public IQueryable<Order> Apply(IQueryable<Order> query) => query.OrderBy(o => o.OrderNumber);
}

file sealed class HighValueOrdersQuery : IPagedQuery<Order>
{
    public required decimal MinTotal { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }

    public IQueryable<Order> Apply(IQueryable<Order> query)
        => query.Where(o => o.Total >= MinTotal).OrderByDescending(o => o.Total);
}

file sealed class PendingOrdersQuery : IQuery<Order>
{
    public IQueryable<Order> Apply(IQueryable<Order> query)
        => query.Where(o => o.Status == OrderStatus.Pending).OrderBy(o => o.OrderNumber);
}

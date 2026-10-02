// Pragmatic.Persistence Query Samples
// Demonstrates the Query infrastructure: QueryBuilder, PagedResult, Adapters, Executors

using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Adapters;
using Pragmatic.Persistence.Query.Executors;
using Pragmatic.Persistence.Query.Interfaces;
using Pragmatic.Persistence.Query.Results;
using Pragmatic.Persistence.Samples.Entities;
using Pragmatic.Specification;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║         Pragmatic.Persistence Query Samples                   ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Create sample data
var orders = SampleData.CreateOrders().AsQueryable();
var customers = SampleData.CreateCustomers().AsQueryable();

// ============================================================================
// 1. Query Entry Point - Query.For<T>()
// ============================================================================
Console.WriteLine("1. Query Entry Point - Query.For<T>()");
Console.WriteLine("   ─────────────────────────────────────");

var simpleQuery = Query.For<Order>().Build(orders);
Console.WriteLine($"   All orders: {simpleQuery.Count()} items");
Console.WriteLine();

// ============================================================================
// 2. QueryBuilder Fluent API - Filtering
// ============================================================================
Console.WriteLine("2. QueryBuilder - Filtering");
Console.WriteLine("   ─────────────────────────────────────");

// Single filter
var shippedOrders = Query.For<Order>()
    .WithFilter(o => o.Status == OrderStatus.Shipped)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Shipped orders: {shippedOrders.Count}");

// Multiple filters (AND)
var highValueShipped = Query.For<Order>()
    .WithFilter(o => o.Status == OrderStatus.Shipped)
    .WithFilter(o => o.Total > 200)
    .Build(orders)
    .ToList();
Console.WriteLine($"   High-value shipped orders (> $200): {highValueShipped.Count}");

// String contains filter
var searchResults = Query.For<Order>()
    .WithFilter(o => o.OrderNumber.Contains("001"))
    .Build(orders)
    .ToList();
Console.WriteLine($"   Orders containing '001': {searchResults.Count}");
Console.WriteLine();

// ============================================================================
// 3. QueryBuilder - Sorting
// ============================================================================
Console.WriteLine("3. QueryBuilder - Sorting");
Console.WriteLine("   ─────────────────────────────────────");

// Single sort ascending
var sortedByTotal = Query.For<Order>()
    .OrderBy(o => o.Total)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Sorted by Total (asc): {string.Join(", ", sortedByTotal.Take(3).Select(o => $"${o.Total}"))}...");

// Single sort descending
var sortedByTotalDesc = Query.For<Order>()
    .OrderByDescending(o => o.Total)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Sorted by Total (desc): {string.Join(", ", sortedByTotalDesc.Take(3).Select(o => $"${o.Total}"))}...");

// Multiple sorts
var multiSorted = Query.For<Order>()
    .OrderBy(o => o.Status)
    .ThenByDescending(o => o.Total)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Multi-sort (Status asc, Total desc): First 3 = {string.Join(", ", multiSorted.Take(3).Select(o => $"{o.Status}/${o.Total:C}"))}");
Console.WriteLine();

// ============================================================================
// 4. QueryBuilder - Paging
// ============================================================================
Console.WriteLine("4. QueryBuilder - Paging");
Console.WriteLine("   ─────────────────────────────────────");

var page1 = Query.For<Order>()
    .OrderBy(o => o.CreatedAt)
    .WithPaging(page: 1, pageSize: 3)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Page 1 (size 3): {page1.Count} items - {string.Join(", ", page1.Select(o => o.OrderNumber))}");

var page2 = Query.For<Order>()
    .OrderBy(o => o.CreatedAt)
    .WithPaging(page: 2, pageSize: 3)
    .Build(orders)
    .ToList();
Console.WriteLine($"   Page 2 (size 3): {page2.Count} items - {string.Join(", ", page2.Select(o => o.OrderNumber))}");
Console.WriteLine();

// ============================================================================
// 5. PagedResult - Result Type with Pagination Metadata
// ============================================================================
Console.WriteLine("5. PagedResult - Pagination Metadata");
Console.WriteLine("   ─────────────────────────────────────");

var pagedResult = PagedResult<Order>.Success(
    page1,
    totalCount: orders.Count(),
    page: 1,
    pageSize: 3);

Console.WriteLine($"   Items: {pagedResult.Items.Count}");
Console.WriteLine($"   Total Count: {pagedResult.TotalCount}");
Console.WriteLine($"   Total Pages: {pagedResult.TotalPages}");
Console.WriteLine($"   Has Next Page: {pagedResult.HasNextPage}");
Console.WriteLine($"   Has Previous Page: {pagedResult.HasPreviousPage}");

// Match pattern for handling success/failure
var message = pagedResult.Match(
    success: (items, total) => $"Found {items.Count} of {total} orders",
    failure: error => $"Error: {error.Code}");
Console.WriteLine($"   Match result: {message}");
Console.WriteLine();

// ============================================================================
// 6. PagedResult - Map Transformation
// ============================================================================
Console.WriteLine("6. PagedResult - Map Transformation");
Console.WriteLine("   ─────────────────────────────────────");

var mappedResult = pagedResult.Select(o => new { o.OrderNumber, o.Total });
Console.WriteLine($"   Mapped to anonymous: {string.Join(", ", mappedResult.Items.Select(x => $"{x.OrderNumber}={x.Total:C}"))}");
Console.WriteLine();

// ============================================================================
// 7. InMemoryQueryExecutor
// ============================================================================
Console.WriteLine("7. InMemoryQueryExecutor");
Console.WriteLine("   ─────────────────────────────────────");

var executor = InMemoryQueryExecutor.Instance;
var customQuery = new GetOrdersByStatus(OrderStatus.Shipped);
var executorResult = await executor.ExecuteAsync(customQuery, orders);

executorResult.Match(
    success: (items, total) =>
    {
        Console.WriteLine($"   Executor found {items.Count} of {total} shipped orders");
        return "ok";
    },
    failure: error =>
    {
        Console.WriteLine($"   Error: {error.Code}");
        return "error";
    });
Console.WriteLine();

// ============================================================================
// 8. DevExpress Grid Adapter
// ============================================================================
Console.WriteLine("8. DevExpress Grid Adapter");
Console.WriteLine("   ─────────────────────────────────────");

var devExpressOptions = new DevExpressLoadOptions
{
    Filter = new List<object?> { "Status", "=", (int)OrderStatus.Shipped },
    Sort = new List<DevExpressSortDescriptor>
    {
        new() { Selector = "Total", Desc = true }
    },
    Skip = 0,
    Take = 3
};

var devExpressResults = Query.For<Order>()
    .FromDevExpress(devExpressOptions)
    .Build(orders)
    .ToList();

Console.WriteLine($"   DevExpress filter (Status=Shipped, Sort=Total desc, Take=3): {devExpressResults.Count} items");
foreach (var order in devExpressResults)
{
    Console.WriteLine($"      - {order.OrderNumber}: {order.Total:C} ({order.Status})");
}
Console.WriteLine();

// ============================================================================
// 9. PrimeNG Grid Adapter
// ============================================================================
Console.WriteLine("9. PrimeNG Grid Adapter");
Console.WriteLine("   ─────────────────────────────────────");

var primeNgEvent = new PrimeNGLazyLoadEvent
{
    Filters = new Dictionary<string, PrimeNGFilterMetadata>
    {
        ["OrderNumber"] = new() { Value = "ORD-00", MatchMode = "startsWith" }
    },
    SortField = "CreatedAt",
    SortOrder = -1, // Descending
    First = 0,
    Rows = 5
};

var primeNgResults = Query.For<Order>()
    .FromPrimeNG(primeNgEvent)
    .Build(orders)
    .ToList();

Console.WriteLine($"   PrimeNG filter (OrderNumber starts with 'ORD-00', Sort=CreatedAt desc): {primeNgResults.Count} items");
foreach (var order in primeNgResults.Take(3))
{
    Console.WriteLine($"      - {order.OrderNumber}: {order.CreatedAt:g}");
}
Console.WriteLine();

// ============================================================================
// 10. Specification Integration
// ============================================================================
Console.WriteLine("10. Specification Integration");
Console.WriteLine("    ─────────────────────────────────────");

Specification<Order> highValueSpec = Spec<Order>.Where(o => o.Total > 200);
Specification<Order> recentSpec = Spec<Order>.Where(o => o.CreatedAt > DateTime.Now.AddDays(-30));

// Combine specifications
var combinedSpec = highValueSpec & recentSpec;

var specResults = orders
    .Where(combinedSpec.ToExpression())
    .ToList();

Console.WriteLine($"   High-value (>$200) AND Recent (<30 days): {specResults.Count} orders");
foreach (var order in specResults.Take(3))
{
    Console.WriteLine($"      - {order.OrderNumber}: {order.Total:C}");
}
Console.WriteLine();

// ============================================================================
// 11. Combined Query Operations
// ============================================================================
Console.WriteLine("11. Combined Query Operations");
Console.WriteLine("    ─────────────────────────────────────");

var complexQuery = Query.For<Order>()
    .WithFilter(o => o.Total >= 100)
    .WithFilter(o => o.Status != OrderStatus.Cancelled)
    .OrderByDescending(o => o.Total)
    .WithPaging(page: 1, pageSize: 5)
    .Build(orders)
    .ToList();

Console.WriteLine($"   Complex query (Total >= $100, Not Cancelled, Top 5 by Total):");
foreach (var order in complexQuery)
{
    Console.WriteLine($"      - {order.OrderNumber}: {order.Total:C} ({order.Status})");
}
Console.WriteLine();

// ============================================================================
// Summary
// ============================================================================
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║                    Features Demonstrated                      ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.WriteLine("║  - Query.For<T>() entry point                                 ║");
Console.WriteLine("║  - QueryBuilder fluent API (filter, sort, page)               ║");
Console.WriteLine("║  - PagedResult with Match and Map                             ║");
Console.WriteLine("║  - InMemoryQueryExecutor                                      ║");
Console.WriteLine("║  - DevExpress grid adapter                                    ║");
Console.WriteLine("║  - PrimeNG grid adapter                                       ║");
Console.WriteLine("║  - Specification integration                                  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

// ============================================================================
// Custom Query Implementation
// ============================================================================

/// <summary>
///     Example of a custom paged query implementation.
/// </summary>
internal class GetOrdersByStatus(OrderStatus status) : IPagedQuery<Order>
{
    public int Page => 1;
    public int PageSize => 10;

    public IQueryable<Order> Apply(IQueryable<Order> query)
        => query.Where(o => o.Status == status);
}

// ============================================================================
// Sample Data
// ============================================================================

internal static class SampleData
{
    public static List<Order> CreateOrders()
    {
        var customerId1 = Guid.NewGuid();
        var customerId2 = Guid.NewGuid();

        return
        [
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-001", Total = 150m, Status = OrderStatus.Pending, CreatedAt = DateTime.Now.AddDays(-10), CustomerId = customerId1 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-002", Total = 250m, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now.AddDays(-8), CustomerId = customerId1 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-003", Total = 75m, Status = OrderStatus.Delivered, CreatedAt = DateTime.Now.AddDays(-6), CustomerId = customerId2 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-004", Total = 500m, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now.AddDays(-4), CustomerId = customerId1 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-005", Total = 125m, Status = OrderStatus.Cancelled, CreatedAt = DateTime.Now.AddDays(-2), CustomerId = customerId2 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-006", Total = 320m, Status = OrderStatus.Processing, CreatedAt = DateTime.Now.AddDays(-1), CustomerId = customerId1 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-007", Total = 89m, Status = OrderStatus.Pending, CreatedAt = DateTime.Now, CustomerId = customerId2 },
            new Order { Id = Guid.NewGuid(), OrderNumber = "ORD-008", Total = 450m, Status = OrderStatus.Shipped, CreatedAt = DateTime.Now.AddHours(-12), CustomerId = customerId1 },
        ];
    }

    public static List<Customer> CreateCustomers()
    {
        return
        [
            new Customer { Id = Guid.NewGuid(), Name = "Acme Corp", Email = "orders@acme.com", Type = CustomerType.Business, IsActive = true, CreatedAt = DateTime.Now.AddMonths(-6) },
            new Customer { Id = Guid.NewGuid(), Name = "John Doe", Email = "john@example.com", Type = CustomerType.Individual, IsActive = true, CreatedAt = DateTime.Now.AddMonths(-3) },
            new Customer { Id = Guid.NewGuid(), Name = "Enterprise Inc", Email = "procurement@enterprise.com", Type = CustomerType.Enterprise, IsActive = true, CreatedAt = DateTime.Now.AddMonths(-12) },
        ];
    }
}

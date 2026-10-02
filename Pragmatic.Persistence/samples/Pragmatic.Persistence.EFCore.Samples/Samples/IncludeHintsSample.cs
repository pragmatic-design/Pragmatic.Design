using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Query;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.Query.Interfaces;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates IIncludableQuery for eager loading and IQueryHints for execution control.
///     Shows tracked vs untracked queries, include paths, and split queries.
/// </summary>
public static class IncludeHintsSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 8. Include & Query Hints ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"IncludeHintsDb_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);

        // Seed customer + order + line
        var customer = new Customer
        {
            PersistenceId = Guid.CreateVersion7(),
            Name = "Include Test Corp",
            Email = "include@test.com"
        };
        db.Customers.Add(customer);

        var product = new Product
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = "INC-001",
            Name = "Include Widget",
            Price = 25.00m,
            IsAvailable = true
        };
        db.Products.Add(product);

        var order = new Order
        {
            PersistenceId = Guid.CreateVersion7(),
            OrderNumber = "INC-ORD-001",
            Status = OrderStatus.Processing,
            Total = 75.00m,
            Customer = customer,
            OrderDate = DateTime.UtcNow
        };
        db.Orders.Add(order);

        var line = new OrderLine
        {
            PersistenceId = Guid.CreateVersion7(),
            Product = product,
            Quantity = 3,
            UnitPrice = 25.00m,
            DiscountPercent = 0
        };
        order.Lines.Add(line);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var executor = new EfCoreQueryExecutor();

        // ── IIncludableQuery — eager loading ──
        Console.WriteLine("  IIncludableQuery — eager-load Customer navigation:");
        var ordersWithCustomer = await executor.ExecuteAllAsync(
            new OrdersWithCustomerQuery(),
            db.Orders);

        foreach (var o in ordersWithCustomer)
            Console.WriteLine($"    [{o.OrderNumber}] Customer: {o.Customer?.Name ?? "(null)"}");
        Console.WriteLine();

        // ── IQueryHints — tracked query ──
        Console.WriteLine("  IQueryHints — tracked query (NoTracking = false):");
        db.ChangeTracker.Clear();

        var trackedOrders = await executor.ExecuteAllAsync(
            new TrackedOrdersQuery(),
            db.Orders);

        Console.WriteLine($"    Loaded {trackedOrders.Count} orders (tracked)");
        Console.WriteLine($"    ChangeTracker entries: {db.ChangeTracker.Entries<Order>().Count()}");
        Console.WriteLine();

        // ── Default — AsNoTracking ──
        Console.WriteLine("  Default — AsNoTracking (read-only):");
        db.ChangeTracker.Clear();

        var untrackedOrders = await executor.ExecuteAllAsync(
            new PendingOrdersQuery(),
            db.Orders);

        Console.WriteLine($"    Loaded {untrackedOrders.Count} orders (untracked)");
        Console.WriteLine($"    ChangeTracker entries: {db.ChangeTracker.Entries<Order>().Count()}");
        Console.WriteLine();

        Console.WriteLine("  Summary:");
        Console.WriteLine("    IIncludableQuery<T>  — IncludePaths => [\"Customer\", \"Lines.Product\"]");
        Console.WriteLine("    IQueryHints          — NoTracking (default: true), SplitQuery, IgnoreGlobalFilters");
        Console.WriteLine();
    }
}

// ── Query implementations ──

file sealed class OrdersWithCustomerQuery : IQuery<Order>, IIncludableQuery<Order>
{
    public IReadOnlyList<string> IncludePaths => ["Customer"];
    public IQueryable<Order> Apply(IQueryable<Order> query) => query.OrderBy(o => o.OrderNumber);
}

file sealed class TrackedOrdersQuery : IQuery<Order>, IQueryHints
{
    public bool NoTracking => false;
    public IQueryable<Order> Apply(IQueryable<Order> query) => query;
}

file sealed class PendingOrdersQuery : IQuery<Order>
{
    public IQueryable<Order> Apply(IQueryable<Order> query)
        => query.Where(o => o.Status == OrderStatus.Pending).OrderBy(o => o.OrderNumber);
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates [Projectable] for SQL-translatable expression projections.
///     Generated Expression&lt;Func&lt;T, TResult&gt;&gt; allows EF Core to push computation to SQL.
/// </summary>
public static class ProjectableSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 7. [Projectable] Expressions ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"ProjectableDb_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);

        // Seed order lines with different quantities, prices, and discounts
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var seededLines = new[]
        {
            new OrderLine { PersistenceId = Guid.NewGuid(), Quantity = 2, UnitPrice = 50.00m, DiscountPercent = 10 },
            new OrderLine { PersistenceId = Guid.NewGuid(), Quantity = 1, UnitPrice = 200.00m, DiscountPercent = 0 },
            new OrderLine { PersistenceId = Guid.NewGuid(), Quantity = 5, UnitPrice = 10.00m, DiscountPercent = 25 }
        };
        foreach (var seeded in seededLines)
        {
            seeded.SetOrderId(orderId);
            seeded.SetProductId(productId);
        }
        db.OrderLines.AddRange(seededLines);
        await db.SaveChangesAsync();

        // Use the generated OrderLine.Expr.Total in a Select projection
        Console.WriteLine("  Select projection — OrderLine.Expr.Total:");
        Console.WriteLine("    Expression<Func<OrderLine, decimal>> translates to SQL");
        Console.WriteLine();

        var totals = await db.OrderLines
            .Select(OrderLine.Expr.Total)
            .ToListAsync();

        foreach (var total in totals)
            Console.WriteLine($"    Line total: {total:C}");
        Console.WriteLine();

        // Use in Where clause via computed expression
        Console.WriteLine("  Where clause — filter by computed total > 100:");
        var highValueLines = await db.OrderLines
            .Where(ol => ol.UnitPrice * ol.Quantity * (1 - ol.DiscountPercent / 100) > 100)
            .Select(OrderLine.Expr.Total)
            .ToListAsync();

        Console.WriteLine($"    High-value lines: {highValueLines.Count}");
        foreach (var total in highValueLines)
            Console.WriteLine($"      {total:C}");

        Console.WriteLine();
        Console.WriteLine("  [Projectable] generates:");
        Console.WriteLine("    OrderLine.Expr.Total => e => e.UnitPrice * e.Quantity * (1 - e.DiscountPercent / 100)");
        Console.WriteLine("    No client-side evaluation — everything runs in SQL!");
        Console.WriteLine();
    }
}

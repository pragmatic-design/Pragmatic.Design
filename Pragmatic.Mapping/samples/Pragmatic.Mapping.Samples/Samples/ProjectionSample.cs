using System.Linq.Expressions;
using Pragmatic.Mapping.Samples.Dtos;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates EF Core-compatible projections using [GenerateProjection].
/// </summary>
public static class ProjectionSample
{
    public static void Run()
    {
        Console.WriteLine("--- Projection Sample ---");

        // The Projection property is an Expression<Func<TEntity, TDto>>
        // which EF Core can translate to SQL SELECT statements

        Console.WriteLine("Generated Projection Expressions:");
        Console.WriteLine();

        // OrderDto.Projection
        Console.WriteLine("OrderDto.Projection:");
        PrintExpression(OrderDto.Projection);

        // OrderSummaryDto.Projection (with flattening)
        Console.WriteLine();
        Console.WriteLine("OrderSummaryDto.Projection:");
        PrintExpression(OrderSummaryDto.Projection);

        // CoordinateDto.Projection (record struct)
        Console.WriteLine();
        Console.WriteLine("CoordinateDto.Projection:");
        PrintExpression(CoordinateDto.Projection);

        // Simulated in-memory query using the projection
        Console.WriteLine();
        Console.WriteLine("In-Memory Query Simulation:");

        var orders = new List<Order>
        {
            new()
            {
                Id = 1,
                OrderNumber = "ORD-001",
                Total = 150.00m,
                Status = OrderStatus.Delivered,
                OrderDate = DateTime.Now.AddDays(-7),
                Customer = new Customer { Id = 1, Name = "John Doe", Email = "john@example.com" }
            },
            new()
            {
                Id = 2,
                OrderNumber = "ORD-002",
                Total = 250.00m,
                Status = OrderStatus.Pending,
                OrderDate = DateTime.Now,
                Customer = new Customer { Id = 2, Name = "Jane Smith", Email = "jane@example.com" }
            }
        };

        // Using the projection with LINQ (simulates EF Core query)
        // In real EF Core: await dbContext.Orders.Select(OrderSummaryDto.Projection).ToListAsync()
        var compiledProjection = OrderSummaryDto.Projection.Compile();
        var summaries = orders.Select(compiledProjection).ToList();

        Console.WriteLine("Order Summaries (via Projection):");
        foreach (var s in summaries)
            Console.WriteLine($"  {s.OrderNumber}: {s.CustomerName} - {s.Total:C} ({s.StatusText})");

        Console.WriteLine();
        Console.WriteLine("Usage with EF Core:");
        Console.WriteLine("  var summaries = await dbContext.Orders");
        Console.WriteLine("      .Where(o => o.Status != OrderStatus.Cancelled)");
        Console.WriteLine("      .OrderByDescending(o => o.OrderDate)");
        Console.WriteLine("      .Select(OrderSummaryDto.Projection)  // Translates to efficient SQL");
        Console.WriteLine("      .ToListAsync();");

        Console.WriteLine();
    }

    private static void PrintExpression<T, TResult>(Expression<Func<T, TResult>> expression)
    {
        // Print a simplified representation of the expression
        var body = expression.Body.ToString();
        var simplified = body
            .Replace(typeof(T).FullName + ".", "entity.")
            .Replace("entity.", "    ");

        Console.WriteLine($"  entity => new {typeof(TResult).Name}");
        Console.WriteLine("  {");

        // Extract member assignments
        if (expression.Body is MemberInitExpression memberInit)
            foreach (var binding in memberInit.Bindings)
                if (binding is MemberAssignment assignment)
                {
                    var value = assignment.Expression.ToString()
                        .Replace(expression.Parameters[0].Name + ".", "entity.");
                    Console.WriteLine($"      {binding.Member.Name} = {value},");
                }

        Console.WriteLine("  }");
    }
}
using Pragmatic.Mapping.Samples.Dtos;
using Pragmatic.Mapping.Samples.Entities;

namespace Pragmatic.Mapping.Samples.Samples;

/// <summary>
///     Demonstrates nested DTO mapping and collection handling.
/// </summary>
public static class NestedMappingSample
{
    public static void Run()
    {
        Console.WriteLine("--- Nested Mapping Sample ---");

        // Create a complex entity with nested objects and collections
        var order = new Order
        {
            Id = 1001,
            OrderNumber = "ORD-2024-001",
            Total = 249.99m,
            Status = OrderStatus.Shipped,
            OrderDate = new DateTime(2024, 6, 15),
            ShippedDate = new DateTime(2024, 6, 17),
            Customer = new Customer
            {
                Id = 42,
                Name = "John Doe",
                Email = "john.doe@example.com"
            },
            Lines =
            [
                new OrderLine { Id = 1, ProductName = "Widget Pro", Quantity = 2, UnitPrice = 49.99m },
                new OrderLine { Id = 2, ProductName = "Gadget X", Quantity = 1, UnitPrice = 150.01m }
            ]
        };

        // Map to DTO - nested Customer and Lines are automatically mapped
        var orderDto = OrderDto.FromEntity(order);

        Console.WriteLine($"Order: {orderDto.OrderNumber}");
        Console.WriteLine($"  Status: {orderDto.Status}");
        Console.WriteLine($"  Total: {orderDto.Total:C}");
        Console.WriteLine($"  Customer: {orderDto.Customer?.Name} ({orderDto.Customer?.Email})");
        Console.WriteLine($"  Lines ({orderDto.Lines.Count}):");
        foreach (var line in orderDto.Lines)
            Console.WriteLine($"    - {line.ProductName} x{line.Quantity} @ {line.UnitPrice:C}");

        // Map to flattened summary - nested properties are flattened
        var summaryDto = OrderSummaryDto.FromEntity(order);

        Console.WriteLine();
        Console.WriteLine("Flattened Summary:");
        Console.WriteLine(
            $"  {summaryDto.OrderNumber}: {summaryDto.CustomerName} - {summaryDto.Total:C} ({summaryDto.StatusText})");

        // Collection mapping
        var orders = new List<Order>
        {
            order,
            new()
            {
                Id = 1002,
                OrderNumber = "ORD-2024-002",
                Total = 75.00m,
                Status = OrderStatus.Pending,
                OrderDate = DateTime.Now,
                Customer = new Customer { Id = 43, Name = "Jane Smith", Email = "jane@example.com" },
                Lines = [new OrderLine { ProductName = "Basic Widget", Quantity = 3, UnitPrice = 25.00m }]
            }
        };

        var summaries = orders.ToOrderSummaryDto().ToList();
        Console.WriteLine();
        Console.WriteLine($"All Orders ({summaries.Count}):");
        foreach (var s in summaries)
            Console.WriteLine($"  {s.OrderNumber}: {s.CustomerName} - {s.Total:C}");

        Console.WriteLine();
    }
}
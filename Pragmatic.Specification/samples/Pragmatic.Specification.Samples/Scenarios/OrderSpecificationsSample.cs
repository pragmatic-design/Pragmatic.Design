
namespace Pragmatic.Specification.Samples.Scenarios;

/// <summary>
///     Demonstrates specifications for a different entity type (Order).
/// </summary>
public class OrderSpecificationsSample : ISample
{
    public string Name => "Order Specifications";
    public string Description => "Specifications for Order entities";

    public void Run()
    {
        var orders = SampleData.Orders;

        Console.WriteLine("Sample orders:");
        foreach (var order in orders)
            Console.WriteLine($"  {order}");
        Console.WriteLine();

        // Simple specifications
        var pendingOrders = orders.Where(OrderSpecs.IsPending()).ToList();
        Console.WriteLine($"Pending orders: {pendingOrders.Count}");

        var completedOrders = orders.Where(OrderSpecs.IsCompleted()).ToList();
        Console.WriteLine($"Completed orders: {completedOrders.Count}");

        // Composed specification: high-value pending orders
        var highValuePending = orders.Where(OrderSpecs.IsHighValuePending()).ToList();
        Console.WriteLine("\nHigh-value pending orders (>$1000):");
        foreach (var order in highValuePending)
            Console.WriteLine($"  Order #{order.Id}: {order.Amount:C}");

        // Custom threshold
        var veryHighValue = orders.Where(OrderSpecs.IsHighValuePending(2000m)).ToList();
        Console.WriteLine("\nVery high-value pending orders (>$2000):");
        foreach (var order in veryHighValue)
            Console.WriteLine($"  Order #{order.Id}: {order.Amount:C}");
    }
}
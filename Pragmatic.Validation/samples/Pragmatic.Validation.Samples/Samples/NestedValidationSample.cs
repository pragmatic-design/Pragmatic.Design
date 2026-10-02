namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Nested collection validation with [ValidateElements] and
///     building nested errors manually with WithNested().
/// </summary>
public static class NestedValidationSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("4. Nested & Collection Validation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowValidOrder();
        ShowInvalidItems();
        ShowEmptyCollection();

        Console.WriteLine();
    }

    private static void ShowValidOrder()
    {
        Console.WriteLine("  4.1 Valid order with valid items");
        Console.WriteLine("  ----------------------------------");

        var order = new CreateOrderRequest
        {
            CustomerId = "cust-123",
            Items =
            [
                new OrderItemRequest { ProductId = "prod-1", Quantity = 3, UnitPrice = 29.99m },
                new OrderItemRequest { ProductId = "prod-2", Quantity = 1, UnitPrice = 99.00m }
            ]
        };

        var result = order.Validate();
        Console.WriteLine($"    {order.Items.Count} valid items → IsSuccess: {result.IsSuccess}");
        Console.WriteLine();
    }

    private static void ShowInvalidItems()
    {
        Console.WriteLine("  4.2 Order with invalid items — [ValidateElements] recursive validation");
        Console.WriteLine("  -----------------------------------------------------------------------");

        var order = new CreateOrderRequest
        {
            CustomerId = "cust-123",
            Items =
            [
                new OrderItemRequest { ProductId = "prod-1", Quantity = 3, UnitPrice = 29.99m },
                new OrderItemRequest { ProductId = "", Quantity = 0, UnitPrice = -5m } // All invalid
            ]
        };

        var result = order.Validate();
        Console.WriteLine($"    Item[0]: valid, Item[1]: ProductId=\"\", Qty=0, Price=-5");
        Console.WriteLine($"    IsFailure: {result.IsFailure}, Count: {result.Count}");
        foreach (var issue in result.Issues)
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();

        Console.WriteLine("    Note: PropertyPath shows Items[1].ProductId, Items[1].Quantity, etc.");
        Console.WriteLine("    The SG recursively validates each element implementing ISyncValidator.");
        Console.WriteLine();
    }

    private static void ShowEmptyCollection()
    {
        Console.WriteLine("  4.3 Empty collection — [MinCount(1)] fails");
        Console.WriteLine("  ---------------------------------------------");

        var order = new CreateOrderRequest
        {
            CustomerId = "cust-123",
            Items = [] // Empty!
        };

        var result = order.Validate();
        Console.WriteLine($"    Items count: 0 (MinCount=1)");
        foreach (var issue in result.Issues.Where(i =>
                     i.PropertyPath == "Items"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }
}

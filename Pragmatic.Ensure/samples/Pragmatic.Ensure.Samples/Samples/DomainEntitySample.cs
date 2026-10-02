namespace Pragmatic.Ensure.Samples.Samples;

/// <summary>
///     Demonstrates Ensure usage in a realistic domain entity scenario.
/// </summary>
public static class DomainEntitySample
{
    public static void Run()
    {
        Console.WriteLine("--- Domain Entity Sample ---\n");

        // Create a valid order
        var order = new Order(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [
                new OrderItem("SKU-001", "Widget", 2, 9.99m),
                new OrderItem("SKU-002", "Gadget", 1, 29.99m)
            ],
            DateTime.UtcNow.AddDays(3));

        Console.WriteLine($"Order created: {order.Id}");
        Console.WriteLine($"  Customer: {order.CustomerId}");
        Console.WriteLine($"  Items: {order.Items.Count}");
        Console.WriteLine($"  Total: ${order.Total:F2}");
        Console.WriteLine($"  Shipping: {order.ShippingDate:d}");

        // Demonstrate validation in service layer
        // Valid discount
        var discounted = OrderService.ApplyDiscount(order, 10);
        Console.WriteLine($"\nAfter 10% discount: ${discounted:F2}");

        // Try invalid discount
        try
        {
            OrderService.ApplyDiscount(order, -5);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.WriteLine($"Caught: {ex.GetType().Name} - discount must be between 0 and 100");
        }

        Console.WriteLine();
    }

    /// <summary>
    ///     Example domain entity: Order
    /// </summary>
    private class Order
    {
        public Order(Guid id, Guid customerId, IReadOnlyList<OrderItem> items, DateTime shippingDate)
        {
            Ensure.ThrowIfEmpty(id);
            Ensure.ThrowIfEmpty(customerId);
            Ensure.ThrowIfNullOrEmpty(items);
            Ensure.ThrowIfInPast(shippingDate);

            Id = id;
            CustomerId = customerId;
            Items = items;
            ShippingDate = shippingDate;
        }

        public Guid Id { get; }
        public Guid CustomerId { get; }
        public IReadOnlyList<OrderItem> Items { get; }
        public DateTime ShippingDate { get; }
        public decimal Total => Items.Sum(i => i.LineTotal);
    }

    /// <summary>
    ///     Example value object: OrderItem
    /// </summary>
    private class OrderItem
    {
        public OrderItem(string sku, string name, int quantity, decimal unitPrice)
        {
            Ensure.ThrowIfNullOrWhiteSpace(sku);
            Ensure.ThrowIfNullOrWhiteSpace(name);
            Ensure.ThrowIfNegativeOrZero(quantity);
            Ensure.ThrowIfNegative(unitPrice);

            Sku = sku;
            Name = name;
            Quantity = quantity;
            UnitPrice = unitPrice;
        }

        public string Sku { get; }
        public string Name { get; }
        public int Quantity { get; }
        public decimal UnitPrice { get; }
        public decimal LineTotal => Quantity * UnitPrice;
    }

    /// <summary>
    ///     Example application service with parameter validation.
    /// </summary>
    private class OrderService
    {
        public static decimal ApplyDiscount(Order order, decimal discountPercent)
        {
            Ensure.ThrowIfNull(order);
            Ensure.ThrowIfOutOfRange(discountPercent, 0m, 100m);

            return order.Total * (1 - discountPercent / 100);
        }
    }
}
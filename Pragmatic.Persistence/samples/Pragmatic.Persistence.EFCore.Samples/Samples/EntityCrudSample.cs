using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates entity attributes and CRUD operations.
///     Shows [Entity], [Auditable], [SoftDelete], [LogicKey] and entity creation.
/// </summary>
public static class EntityCrudSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 1. Entity Attributes & CRUD ═══");
        Console.WriteLine();

        // Entity attribute overview
        Console.WriteLine("  Entity Attributes:");
        Console.WriteLine("    [Entity]  — domain entity with typed identifier");
        Console.WriteLine("    [Auditable]     — CreatedAt, CreatedBy, UpdatedAt, UpdatedBy");
        Console.WriteLine("    [SoftDelete]    — IsDeleted, DeletedAt, DeletedBy");
        Console.WriteLine("    [LogicKey]      — business key uniqueness constraint");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"EntityCrudDb_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);

        // Create customer with all attributes
        var customer = new Customer
        {
            PersistenceId = Guid.CreateVersion7(),
            Email = "john@example.com",
            Name = "John Doe",
            Phone = "+1-555-123-4567",
            Type = CustomerType.Individual,
            IsActive = true
        };
        db.Add(customer);

        Console.WriteLine($"  Customer created:");
        Console.WriteLine($"    Id:    {customer.PersistenceId}");
        Console.WriteLine($"    Email: {customer.Email} (LogicKey)");
        Console.WriteLine($"    Name:  {customer.Name}");
        Console.WriteLine($"    Type:  {customer.Type}");
        Console.WriteLine();

        // Create product with soft delete
        var product = new Product
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = "WIDGET-001",
            Name = "Premium Widget",
            Description = "A high-quality widget",
            Price = 29.99m,
            StockQuantity = 100,
            IsAvailable = true
        };
        db.Add(product);

        Console.WriteLine($"  Product created:");
        Console.WriteLine($"    Sku:   {product.Sku} (LogicKey)");
        Console.WriteLine($"    Name:  {product.Name}");
        Console.WriteLine($"    Price: {product.Price:C}");
        Console.WriteLine();

        // Create order with lines
        var order = new Order
        {
            PersistenceId = Guid.CreateVersion7(),
            OrderNumber = "ORD-2024-001",
            Status = OrderStatus.Pending,
            Customer = customer,
            OrderDate = DateTime.UtcNow
        };
        db.Add(order);

        var orderLine = new OrderLine
        {
            PersistenceId = Guid.CreateVersion7(),
            Order = order,
            Product = product,
            Quantity = 2,
            UnitPrice = product.Price,
            DiscountPercent = 10
        };
        order.Lines.Add(orderLine);
        order.Total = orderLine.Total;

        await db.SaveChangesAsync();

        Console.WriteLine($"  Order created:");
        Console.WriteLine($"    OrderNumber: {order.OrderNumber} (LogicKey)");
        Console.WriteLine($"    Status:      {order.Status}");
        Console.WriteLine($"    Total:       {order.Total:C}");
        Console.WriteLine($"    Lines:       {order.Lines.Count}");
        Console.WriteLine();
    }
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.EFCore.Samples.Mutations;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates Patch DTOs: basic patching, nested patches, and collection sync.
///     [Patch&lt;T&gt;] generates ApplyPatch with property-level tracking.
/// </summary>
public static class PatchSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ 2. Patch DTOs ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"PatchDb_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);

        // Seed data
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

        var address = new Address
        {
            PersistenceId = Guid.CreateVersion7(),
            Street1 = "123 Main St",
            City = "Seattle",
            State = "WA",
            PostalCode = "98101",
            Country = "USA"
        };
        address.SetCustomerId(customer.PersistenceId);
        db.Add(address);

        var order = new Order
        {
            PersistenceId = Guid.CreateVersion7(),
            OrderNumber = "ORD-2024-001",
            Status = OrderStatus.Pending,
            Customer = customer,
            ShippingAddress = address,
            OrderDate = DateTime.UtcNow
        };
        db.Add(order);
        await db.SaveChangesAsync();

        // ── Basic Patch ──
        Console.WriteLine("  Basic Patch — partial property updates:");
        Console.WriteLine($"    Before: Name={customer.Name}, Phone={customer.Phone}");

        var updateCustomer = new UpdateCustomerDto
        {
            Name = "John Smith",
            Phone = "+1-555-999-8888",
            Type = CustomerType.Business
        };
        updateCustomer.ApplyPatch(customer);

        Console.WriteLine($"    After:  Name={customer.Name}, Phone={customer.Phone}, Type={customer.Type}");
        Console.WriteLine();

        // ── Nested Patch ──
        Console.WriteLine("  Nested Patch — order with shipping address:");
        Console.WriteLine($"    Before: Status={order.Status}, Address={order.ShippingAddress!.Street1}, {order.ShippingAddress.City}");

        var updateOrderWithAddress = new UpdateOrderWithAddressDto
        {
            Status = OrderStatus.Shipped,
            Notes = "Package dispatched",
            ShippingAddress = new UpdateAddressDto
            {
                Street1 = "456 Oak Avenue",
                City = "Portland"
            }
        };
        updateOrderWithAddress.ApplyPatch(order);

        Console.WriteLine($"    After:  Status={order.Status}, Address={order.ShippingAddress.Street1}, {order.ShippingAddress.City}");
        Console.WriteLine();

        // ── Collection: matched elements are updated ──
        // ⚠️ Not add/update/remove. The element is a [Patch<OrderLine>], so it can update a line that
        // is already there and cannot build one that is not — PRAG2205 states it at compile time.
        Console.WriteLine("  Collection — patch-only elements update what already exists:");

        var order2 = new Order
        {
            PersistenceId = Guid.CreateVersion7(),
            OrderNumber = "ORD-2024-002",
            Status = OrderStatus.Pending,
            OrderDate = DateTime.UtcNow
        };
        order2.SetCustomerId(customer.PersistenceId);
        db.Add(order2);

        var existingLine = new OrderLine
        {
            PersistenceId = Guid.CreateVersion7(),
            Quantity = 1,
            UnitPrice = 9.99m
        };
        order2.Lines.Add(existingLine);

        Console.WriteLine($"    Before: Lines count = {order2.Lines.Count}");

        var updateOrderWithLines = new UpdateOrderWithLinesDto
        {
            Status = OrderStatus.Processing,
            Notes = "Processing with new lines",
            Lines =
            [
                // Matched by Id: updated in place.
                new UpdateOrderLineDto { Id = existingLine.PersistenceId, Quantity = 5, UnitPrice = 19.99m },
                // Matching nothing: skipped, not created. PRAG2205.
                new UpdateOrderLineDto { Quantity = 3, UnitPrice = 29.99m }
            ]
        };
        updateOrderWithLines.ApplyPatch(order2);

        Console.WriteLine($"    After:  Lines count = {order2.Lines.Count}");
        foreach (var line in order2.Lines)
            Console.WriteLine($"      Qty={line.Quantity}, Price={line.UnitPrice:C}");
        Console.WriteLine();

        await PatchARowReadBackAsync(db, order2.PersistenceId);
    }

    /// <summary>
    ///     Patching a row that came back from the database, loading what the patch writes.
    /// </summary>
    /// <remarks>
    ///     A patch merges: it decides what to remove by looking at what is on the entity, so against a
    ///     collection nobody loaded it removes nothing and adds everything — three lines become six on
    ///     a patch that sent the same three back. What has to be loaded is what the patch writes, and
    ///     the patch publishes exactly that in <c>WrittenNavigations</c>, deep and prefixed. A caller
    ///     holding a repository passes the same list to <c>INavigationLoader.EnsureLoadedAsync</c>.
    /// </remarks>
    private static async Task PatchARowReadBackAsync(SampleDbContext db, Guid orderId)
    {
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        Console.WriteLine("  Patching a row read back — load what the patch writes:");
        Console.WriteLine($"    WrittenNavigations = [{string.Join(", ", UpdateOrderWithLinesDto.WrittenNavigations)}]");

        var query = db.Set<Order>().AsQueryable();
        foreach (var navigation in UpdateOrderWithLinesDto.WrittenNavigations)
            query = query.Include(navigation);

        var reloaded = await query.FirstAsync(o => o.PersistenceId == orderId);

        var samePayload = new UpdateOrderWithLinesDto
        {
            Status = OrderStatus.Processing,
            Lines = reloaded.Lines
                .Select(line => new UpdateOrderLineDto
                {
                    Id = line.PersistenceId,
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice
                })
                .ToList()
        };
        samePayload.ApplyPatch(reloaded);

        Console.WriteLine($"    Lines after a patch that sent the same ones back: {reloaded.Lines.Count}");
        Console.WriteLine();
    }
}

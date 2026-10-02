using Pragmatic.Persistence.EFCore.Samples.Entities;
using Pragmatic.Persistence.EFCore.Samples.Mutations;

namespace Pragmatic.Persistence.EFCore.Samples.Samples;

/// <summary>
///     Demonstrates [MapConverter], [MapProperty], and [MapIgnore] in patch DTOs.
///     Custom type conversions and property name remapping during patching.
/// </summary>
public static class MutationConvertersSample
{
    public static void Run()
    {
        Console.WriteLine("═══ 3. MapConverter & MapProperty ═══");
        Console.WriteLine();

        // ── MapConverter — custom type conversion ──
        Console.WriteLine("  [MapConverter<T>] — custom type conversion:");

        var customer = new Customer
        {
            PersistenceId = Guid.CreateVersion7(),
            Email = "jane@example.com",
            Name = "Jane Doe",
            Type = CustomerType.Individual,
            IsActive = true
        };

        Console.WriteLine($"    Before: Type={customer.Type}, IsActive={customer.IsActive}");

        var converterMutation = new CustomerMutationWithConvertersDto
        {
            Name = "Jane Smith",
            Type = 2,          // int → CustomerType.Business via MapConverter
            IsActive = "yes"   // string → bool via MapConverter
        };
        converterMutation.ApplyPatch(customer);

        Console.WriteLine($"    After:  Type={customer.Type} (from int: 2), IsActive={customer.IsActive} (from string: \"yes\")");
        Console.WriteLine();

        // ── MapProperty — property name remapping ──
        Console.WriteLine("  [MapProperty] — property name remapping:");

        var product = new Product
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = "GADGET-001",
            Name = "Standard Gadget",
            Description = "Original description",
            Price = 49.99m,
            StockQuantity = 50,
            IsAvailable = true
        };

        Console.WriteLine($"    Before: Description=\"{product.Description}\", Stock={product.StockQuantity}");

        var advancedMutation = new AdvancedProductMutationDto
        {
            Name = "Premium Gadget",
            ProductDescription = "Updated via ProductDescription → Description",  // [MapProperty("Description")]
            AvailableStock = 100,                                                  // [MapProperty("StockQuantity")]
            InStock = false,                                                       // [MapProperty("IsAvailable")]
            InternalNotes = "This is ignored"                                      // [MapIgnore]
        };
        advancedMutation.ApplyPatch(product);

        Console.WriteLine($"    After:  Description=\"{product.Description}\"");
        Console.WriteLine($"            Stock={product.StockQuantity}, Available={product.IsAvailable}");
        Console.WriteLine($"            (InternalNotes was [MapIgnore] — not applied)");
        Console.WriteLine();
    }
}

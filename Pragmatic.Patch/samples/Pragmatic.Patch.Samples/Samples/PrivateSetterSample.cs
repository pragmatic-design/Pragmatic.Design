namespace Pragmatic.Patch.Samples.Samples;

/// <summary>
///     Private setter handling: SG detects SetXxx() methods and uses them
///     instead of direct property assignment. Maintains encapsulation.
/// </summary>
public static class PrivateSetterSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("5. Private Setter — SetXxx() Method Resolution");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowPrivateSetterUsage();
        ShowValueTypePatching();

        Console.WriteLine();
    }

    private static void ShowPrivateSetterUsage()
    {
        Console.WriteLine("  5.1 Private setter with SetStock() method");
        Console.WriteLine("  ----------------------------------------------");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Widget",
            Price = 9.99m
        };
        product.SetStock(50);

        Console.WriteLine($"    Before: Stock={product.Stock} (private set)");

        // Patch uses SetStock() method, not direct assignment
        var patch = new PatchProduct { Stock = 100 };
        patch.ApplyTo(product);

        Console.WriteLine($"    After:  Stock={product.Stock} (via SetStock method)");
        Console.WriteLine();
        Console.WriteLine("    The SG generates: entity.SetStock(Stock.Value)");
        Console.WriteLine("    NOT: entity.Stock = Stock.Value (won't compile — private setter)");
        Console.WriteLine();
    }

    private static void ShowValueTypePatching()
    {
        Console.WriteLine("  5.2 Value types (int, decimal) — no accidental zeroing");
        Console.WriteLine("  ----------------------------------------------------------");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = "Gadget",
            Price = 49.99m
        };
        product.SetStock(25);

        // Only update Name — Price and Stock should NOT be zeroed
        var patch = new PatchProduct { Name = "Gadget Pro" };

        Console.WriteLine($"    Before: Name=\"{product.Name}\", Price={product.Price}, Stock={product.Stock}");
        Console.WriteLine($"    Patch:  Name=\"Gadget Pro\", Price=Undefined, Stock=Undefined");

        patch.ApplyTo(product);

        Console.WriteLine($"    After:  Name=\"{product.Name}\", Price={product.Price} (unchanged), Stock={product.Stock} (unchanged)");
        Console.WriteLine();
        Console.WriteLine("    Without Optional<T>, sending { \"name\": \"Gadget Pro\" } would");
        Console.WriteLine("    set Price=0 and Stock=0 (JSON defaults). Tri-state prevents this.");
        Console.WriteLine();
    }
}

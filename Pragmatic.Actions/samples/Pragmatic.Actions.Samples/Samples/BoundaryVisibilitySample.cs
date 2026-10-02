using Pragmatic.Actions.Attributes;
using Pragmatic.Actions.Samples.Boundaries;
using Pragmatic.Actions.Samples.Entities;

namespace Pragmatic.Actions.Samples.Samples;

/// <summary>
///     BoundaryVisibility.Internal and [ReadAccess&lt;T&gt;] cross-boundary DbSet configuration.
///     Both are compile-time directives consumed by the source generators. This sample uses the real
///     <see cref="BoundaryVisibility" /> enum and the real <see cref="ReadAccessAttribute{TEntity}" />
///     type, and explains what each generator emits.
/// </summary>
public static class BoundaryVisibilitySample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("7. Boundary Visibility & Cross-Boundary Read Access");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowVisibility();
        ShowReadAccess();

        Console.WriteLine();
    }

    private static void ShowVisibility()
    {
        Console.WriteLine("  7.1 [Boundary(Visibility = BoundaryVisibility.Internal)]");
        Console.WriteLine("  -----------------------------------------------------------");

        // Real BoundaryVisibility enum values.
        foreach (var value in Enum.GetValues<BoundaryVisibility>())
            Console.WriteLine($"    BoundaryVisibility.{value}");

        Console.WriteLine();
        Console.WriteLine($"    InventoryBoundary intended visibility = {InventoryBoundary.IntendedVisibility}");
        Console.WriteLine();
        Console.WriteLine("    Public   → generates public + internal interfaces; endpoint/remote capable.");
        Console.WriteLine("    Internal → generates ONLY the internal interface.");
        Console.WriteLine("               Actions are intra-assembly only:");
        Console.WriteLine("               - NOT exposable via [Endpoint] / [ExposeEndpoint]");
        Console.WriteLine("               - NOT usable as a remote boundary");
        Console.WriteLine();
        Console.WriteLine("""
            [Boundary(Visibility = BoundaryVisibility.Internal)]
            public partial class InventoryBoundary { }
        """);
        Console.WriteLine();
    }

    private static void ShowReadAccess()
    {
        Console.WriteLine("  7.2 [ReadAccess<Order>] — cross-boundary DbSet (JOIN access)");
        Console.WriteLine("  ---------------------------------------------------------------");

        // The generic attribute TYPE is real and closes over the entity type.
        var readAccessType = typeof(ReadAccessAttribute<Order>);
        Console.WriteLine($"    Attribute type : {readAccessType.Name} closed over {InventoryBoundary.ReadAccessEntity.Name}");
        Console.WriteLine();
        Console.WriteLine("""
            [Boundary(Visibility = BoundaryVisibility.Internal)]
            [ReadAccess<Order>]
            public partial class InventoryBoundary { }

            // Persistence.EFCore generator adds to InventoryBoundary's DbContext:
            //   modelBuilder.Entity<Order>().ToTable("Orders").ExcludeFromMigrations();
            //
            // Effect:
            //   - Inventory queries can JOIN to Order rows (read-only)
            //   - Migrations are NOT duplicated (Order is owned by the Orders boundary)
            //   - Composition validates both boundaries map to the SAME physical database
            //     (PRAG0601 / PRAG0602)
        """);
        Console.WriteLine();
    }
}

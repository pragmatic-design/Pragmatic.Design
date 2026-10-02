using Microsoft.EntityFrameworkCore;

namespace Pragmatic.Persistence.EFCore.Samples.Inheritance;

/// <summary>
///     Demonstrates <c>[Inheritance(InheritanceStrategy.Tph)]</c> on <see cref="Charge"/>:
///     base + derived types share one table with a generated <c>ChargeType</c> discriminator.
///     Querying the base <c>DbSet&lt;Charge&gt;</c> returns the correct concrete subtypes, and
///     <c>OfType&lt;T&gt;()</c> filters to one branch.
/// </summary>
public static class InheritanceSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Inheritance Mapping ([Inheritance] TPH) ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<InheritanceDbContext>()
            .UseInMemoryDatabase($"Inheritance_{Guid.NewGuid():N}")
            .Options;

        await using var db = new InheritanceDbContext(options);

        db.Charges.AddRange(
            new LateFee { Description = "Invoice #42 late", Amount = 25m, DaysLate = 12 },
            new LateFee { Description = "Invoice #51 late", Amount = 25m, DaysLate = 3 },
            new UsageCharge { Description = "API calls", Amount = 80m, Units = 8000, UnitRate = 0.01m });
        await db.SaveChangesAsync();

        // Base query — EF materializes the right concrete type per row from the discriminator.
        var all = await db.Charges.ToListAsync();
        Console.WriteLine($"  Total charges (one table)   : {all.Count}");
        foreach (var c in all)
            Console.WriteLine($"    {c.GetType().Name,-12} {c.Amount,8:C}  {c.Description}");
        Console.WriteLine();

        // Typed filtering via OfType<T>() — translates to a discriminator predicate.
        var lateFees = await db.Charges.OfType<LateFee>().ToListAsync();
        var usage = await db.Charges.OfType<UsageCharge>().ToListAsync();
        Console.WriteLine($"  OfType<LateFee>()           : {lateFees.Count} (expects 2)");
        Console.WriteLine($"  OfType<UsageCharge>()       : {usage.Count} (expects 1)");
        Console.WriteLine($"    LateFee max DaysLate      : {lateFees.Max(f => f.DaysLate)}");
        Console.WriteLine();
    }
}

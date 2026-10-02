using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Lookups;

/// <summary>
///     Demonstrates the <c>[Lookup]</c> / <see cref="ILookupCache{T,TId}"/> contract: small reference
///     tables preloaded once and resolved synchronously by ID, with no per-call DB query. The host
///     wires the generated cache + preload service automatically; here we populate an in-memory
///     implementation to show the resolution semantics.
/// </summary>
public static class LookupSample
{
    public static void Run()
    {
        Console.WriteLine("═══ Lookup ([Lookup] + ILookupCache) ═══");
        Console.WriteLine();

        var eur = new Currency { Code = "EUR", Name = "Euro", Symbol = "€" };
        var usd = new Currency { Code = "USD", Name = "US Dollar", Symbol = "$" };
        var gbp = new Currency { Code = "GBP", Name = "Pound Sterling", Symbol = "£" };

        ILookupCache<Currency, Guid> cache = new InMemoryCurrencyCache([eur, usd, gbp]);

        Console.WriteLine($"  Preloaded entries : {cache.GetAll().Count}");
        foreach (var c in cache.GetAll())
            Console.WriteLine($"    {c.Code}  {c.Symbol}  {c.Name}");
        Console.WriteLine();

        // Synchronous resolution — what a generated navigation property does internally.
        var resolved = cache.Get(usd.Id);
        Console.WriteLine($"  Get(usd.Id)        : {resolved.Code} ({resolved.Name})");

        var found = cache.TryGet(gbp.Id, out var gbpValue);
        Console.WriteLine($"  TryGet(gbp.Id)     : {found} → {gbpValue?.Symbol}");

        var missing = cache.TryGet(Guid.NewGuid(), out _);
        Console.WriteLine($"  TryGet(unknown)    : {missing} (expects False)");
        Console.WriteLine();
    }
}

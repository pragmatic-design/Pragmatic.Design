using Microsoft.EntityFrameworkCore;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.EFCore.Samples.Entities;

namespace Pragmatic.Persistence.EFCore.Samples.Querying;

/// <summary>
///     Demonstrates the three query-shaping building blocks over <see cref="Product"/>:
///     <list type="number">
///         <item><c>[FilterDto&lt;Product&gt;]</c> → generated <c>ApplyFilter</c> (optional, null-skipping criteria).</item>
///         <item><c>[GridFilter&lt;Product&gt;]</c> → generated <c>Apply</c> (dynamic operators + sort + paging).</item>
///         <item><c>IGridFilterAdapter</c> → maps an external grid payload to the canonical <c>GridFilterRequest</c>.</item>
///     </list>
/// </summary>
public static class GridAndFilterDtoSample
{
    public static async Task RunAsync()
    {
        Console.WriteLine("═══ Grid Filter + Filter DTO + Adapter ═══");
        Console.WriteLine();

        var options = new DbContextOptionsBuilder<SampleDbContext>()
            .UseInMemoryDatabase($"Querying_{Guid.NewGuid():N}")
            .Options;

        await using var db = new SampleDbContext(options);
        db.Products.AddRange(
            Product("WIDGET-STD", "Standard Widget", 19.99m, available: true),
            Product("WIDGET-PRO", "Pro Widget", 49.99m, available: true),
            Product("GADGET-01", "Shiny Gadget", 99.00m, available: false),
            Product("WIDGET-MAX", "Widget Max", 149.00m, available: true));
        await db.SaveChangesAsync();

        // ── 1. FilterDto: optional criteria, null props skipped (generated ApplyFilter) ──
        var dto = new ProductFilterDto { Name = "Widget", IsAvailable = true, MaxPrice = 100m };
        var dtoResults = await db.Products.AsNoTracking().ApplyFilter(dto).ToListAsync();
        Console.WriteLine("  [FilterDto] Name~'Widget' AND Available AND Price<=100:");
        foreach (var p in dtoResults)
            Console.WriteLine($"    {p.Name,-18} {p.Price,8:C}  available={p.IsAvailable}");
        Console.WriteLine($"    → {dtoResults.Count} match(es) (expects Standard + Pro)");
        Console.WriteLine();

        // ── 2. GridFilter: dynamic operator + sort + paging (generated Apply) ──
        var grid = new ProductGridFilter
        {
            Name = "Widget",
            NameOperator = StringOperator.StartsWith,
            IsAvailable = true,
            PriceSort = SortDirection.Descending,
            Page = 1,
            PageSize = 2
        };
        var gridResults = await grid.Apply(db.Products.AsNoTracking()).ToListAsync();
        Console.WriteLine("  [GridFilter] Name StartsWith 'Widget' AND Available, sort Price desc, page 1 size 2:");
        foreach (var p in gridResults)
            Console.WriteLine($"    {p.Name,-18} {p.Price,8:C}");
        Console.WriteLine($"    → {gridResults.Count} row(s) on this page");
        Console.WriteLine();

        // ── 3. Adapter: external grid payload → canonical GridFilterRequest ──
        var adapter = new SimpleGridAdapter();
        var external = new ExternalGridRequest(SearchText: "Gadget", Skip: 0, Take: 10, SortByPriceDesc: true);
        var canonical = adapter.Adapt(external);
        Console.WriteLine("  [IGridFilterAdapter] external request → canonical GridFilterRequest:");
        Console.WriteLine($"    Filters : {string.Join(", ", canonical.Filters.Select(f => $"{f.Field} {f.Operator} '{f.Value}'"))}");
        Console.WriteLine($"    Sorts   : {string.Join(", ", canonical.Sorts.Select(s => $"{s.Field} {s.Direction}"))}");
        Console.WriteLine($"    Paging  : page {canonical.Page}, size {canonical.PageSize}");
        Console.WriteLine();
    }

    private static Product Product(string sku, string name, decimal price, bool available)
        => new()
        {
            PersistenceId = Guid.CreateVersion7(),
            Sku = sku,
            Name = name,
            Price = price,
            IsAvailable = available,
            StockQuantity = 10
        };
}

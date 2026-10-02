using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.Samples.Scenarios.EFCore;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Samples.Scenarios;

/// <summary>
///     Demonstrates the EF Core value converters via a real in-memory SQLite round-trip.
///     An entity with a <see cref="CurrencyCode"/> property is persisted and read back,
///     proving the converter maps the typed code to/from a varchar(3) column.
/// </summary>
public static class EFCoreConvertersSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("EF CORE VALUE CONVERTERS");
        Console.WriteLine("   CurrencyCode persisted via SQLite (ApplyPragmaticInternationalization)");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        // In-memory SQLite: a real relational provider, kept alive by the open connection.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite(connection)
            .Options;

        using (var ctx = new CatalogDbContext(options))
        {
            ctx.Database.EnsureCreated();

            ctx.Products.Add(new ProductEntity { Name = "Mechanical Keyboard", Currency = CurrencyCode.FromCode("EUR") });
            ctx.Products.Add(new ProductEntity { Name = "Wireless Mouse", Currency = CurrencyCode.FromCode("USD") });
            ctx.SaveChanges();
        }

        using (var ctx = new CatalogDbContext(options))
        {
            foreach (var product in ctx.Products.OrderBy(p => p.Id))
            {
                // Currency was stored as a string column and rehydrated to the typed CurrencyCode.
                Console.WriteLine($"  {product.Name}: {product.Currency.Code} ({product.Currency.Symbol})");
            }
        }

        // Prove the stored value is a plain 3-char string at the DB level.
        using (var raw = connection.CreateCommand())
        {
            raw.CommandText = "SELECT Name, Currency FROM Products ORDER BY Id";
            using var reader = raw.ExecuteReader();
            Console.WriteLine();
            Console.WriteLine("  Raw column values (varchar):");
            while (reader.Read())
                Console.WriteLine($"    {reader.GetString(0)} -> \"{reader.GetString(1)}\"");
        }

        Console.WriteLine();
    }
}

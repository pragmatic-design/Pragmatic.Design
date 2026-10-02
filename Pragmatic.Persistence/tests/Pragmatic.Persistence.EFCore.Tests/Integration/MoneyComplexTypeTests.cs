using System.Linq;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.Types;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     Validates the EF Core complex-type mapping the SG emits for <c>Money</c> (#1): the model builds, a Money
///     value round-trips through a real (SQLite) database, and — the headline win over the old opaque
///     ValueConverter→string — a predicate over <c>Money.Amount</c> translates to SQL.
/// </summary>
public class MoneyComplexTypeTests
{
    private sealed class Product
    {
        public int Id { get; set; }
        public Money Price { get; private set; }

        public static Product Create(int id, Money price) => new() { Id = id, Price = price };
    }

    private sealed class MoneyDbContext(DbContextOptions<MoneyDbContext> options) : DbContext(options)
    {
        public DbSet<Product> Products => Set<Product>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            var entity = builder.Entity<Product>();
            entity.HasKey(p => p.Id);

            // Mirrors EntityConfigurationTemplate.RenderMoneyComplexProperty exactly.
            entity.ComplexProperty(p => p.Price, b =>
            {
                b.IsRequired();
                b.Property(m => m.Amount).HasColumnName("Price_Amount").HasPrecision(18, 2);
                b.Property(m => m.Currency).HasColumnName("Price_Currency").HasMaxLength(3).HasConversion(
                    c => c.Code,
                    s => CurrencyCode.FromCode(s));
            });
        }
    }

    [Fact]
    public async Task Money_ComplexType_RoundTrips_AndProjectsAmountInSql()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        try
        {
            var options = new DbContextOptionsBuilder<MoneyDbContext>()
                .UseSqlite(connection)
                .Options;

            using (var ctx = new MoneyDbContext(options))
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Products.Add(Product.Create(1, Money.From(99.99m, CurrencyCode.USD)));
                ctx.Products.Add(Product.Create(2, Money.From(10.00m, CurrencyCode.EUR)));
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new MoneyDbContext(options))
            {
                var product = await ctx.Products.SingleAsync(p => p.Id == 1);
                product.Price.Amount.Should().Be(99.99m);
                product.Price.Currency.Code.Should().Be("USD");

                // The whole point of a complex type: a predicate over Money.Amount translates to SQL.
                var expensive = await ctx.Products.Where(p => p.Price.Amount > 50m).CountAsync();
                expensive.Should().Be(1);
            }
        }
        finally
        {
            connection.Close();
        }
    }
}

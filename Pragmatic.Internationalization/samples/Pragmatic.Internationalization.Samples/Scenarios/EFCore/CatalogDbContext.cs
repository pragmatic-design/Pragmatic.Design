using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

namespace Pragmatic.Internationalization.Samples.Scenarios.EFCore;

/// <summary>
///     DbContext that applies the Pragmatic.Internationalization value converters.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<ProductEntity> Products => Set<ProductEntity>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Registers the CurrencyCode value converter (varchar(3)) at the type level,
        // so EF Core maps every CurrencyCode property across the model.
        configurationBuilder.ApplyPragmaticInternationalizationConventions();
    }
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>
///     The setup the module's documentation shows: the entity named, nothing declared about its <c>LocalizedString</c> properties, then
///     <see cref="ModelBuilderExtensions.ApplyPragmaticInternationalization" />.
/// </summary>
public sealed class UndeclaredArticleDbContext(DbContextOptions<UndeclaredArticleDbContext> options) : DbContext(options)
{
    public DbSet<UndeclaredArticle> Articles => Set<UndeclaredArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UndeclaredArticle>();
        modelBuilder.ApplyPragmaticInternationalization();
    }
}

using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>Configured through <see cref="ModelBuilderExtensions.ApplyPragmaticInternationalization" />.</summary>
/// <remarks>
///     The property is declared, as the generated entity configuration declares it before the generated context calls
///     <c>ApplyPragmaticInternationalization</c>. The undeclared case is <see cref="UndeclaredArticleDbContext" />.
/// </remarks>
public sealed class ModelBuilderArticleDbContext(DbContextOptions<ModelBuilderArticleDbContext> options) : DbContext(options)
{
    public DbSet<TranslatedArticle> Articles => Set<TranslatedArticle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TranslatedArticle>().Property(article => article.Title);
        modelBuilder.ApplyPragmaticInternationalization();
    }
}

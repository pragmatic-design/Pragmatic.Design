using Microsoft.EntityFrameworkCore;
using Pragmatic.Internationalization.EntityFrameworkCore.Extensions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>Configured through <see cref="ModelBuilderExtensions.ApplyPragmaticInternationalizationConventions" />.</summary>
public sealed class ConventionsArticleDbContext(DbContextOptions<ConventionsArticleDbContext> options) : DbContext(options)
{
    public DbSet<TranslatedArticle> Articles => Set<TranslatedArticle>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        => configurationBuilder.ApplyPragmaticInternationalizationConventions();
}

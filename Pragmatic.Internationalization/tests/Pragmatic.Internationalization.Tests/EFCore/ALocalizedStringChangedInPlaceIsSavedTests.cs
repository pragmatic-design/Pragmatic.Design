using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>
///     <see cref="LocalizedString.Set" />, <see cref="LocalizedString.Remove" /> and <see cref="LocalizedString.Clear" /> change the instance
///     in place (#623). EF Core detects a change by comparing the current value with a snapshot taken at load, so the column needs a comparer
///     that compares by value and snapshots a copy: with the instance itself as its snapshot, a change in place compares equal and is not saved.
///     Both configuration paths are covered, the conventions and the model builder.
/// </summary>
public sealed class ALocalizedStringChangedInPlaceIsSavedTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ALocalizedStringChangedInPlaceIsSavedTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void Set_OnALoadedEntity_IsSaved_ThroughTheConventions()
    {
        var reloaded = ChangeInPlaceAndReload<ConventionsArticleDbContext>(
            options => new ConventionsArticleDbContext(options),
            title => title.Set("it", "Benvenuti"));

        reloaded.GetExact("it").Should().Be("Benvenuti");
        reloaded.GetExact("en").Should().Be("Welcome");
    }

    [Fact]
    public void Set_OnALoadedEntity_IsSaved_ThroughTheModelBuilder()
    {
        var reloaded = ChangeInPlaceAndReload<ModelBuilderArticleDbContext>(
            options => new ModelBuilderArticleDbContext(options),
            title => title.Set("it", "Benvenuti"));

        reloaded.GetExact("it").Should().Be("Benvenuti");
        reloaded.GetExact("en").Should().Be("Welcome");
    }

    [Fact]
    public void Remove_OnALoadedEntity_IsSaved()
    {
        var reloaded = ChangeInPlaceAndReload<ConventionsArticleDbContext>(
            options => new ConventionsArticleDbContext(options),
            title => title.Remove("fr"));

        reloaded.HasCulture("fr").Should().BeFalse();
        reloaded.GetExact("en").Should().Be("Welcome");
    }

    [Fact]
    public void Clear_OnALoadedEntity_IsSaved()
    {
        var reloaded = ChangeInPlaceAndReload<ConventionsArticleDbContext>(
            options => new ConventionsArticleDbContext(options),
            title => title.Clear());

        reloaded.IsEmpty.Should().BeTrue();
    }

    /// <summary>The control case: a comparer that reports every value as changed would pass the four tests above.</summary>
    [Fact]
    public void AnUnchangedTitle_IsNotModified()
    {
        var options = Options<ConventionsArticleDbContext>(throwOnMissingComparer: false);
        using (var seed = new ConventionsArticleDbContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Articles.Add(new TranslatedArticle { Title = LocalizedString.From(("en", "Welcome")) });
            seed.SaveChanges();
        }

        using var context = new ConventionsArticleDbContext(options);
        var article = context.Articles.Single();
        article.Title.Get("en").Should().Be("Welcome");

        context.ChangeTracker.DetectChanges();
        context.Entry(article).State.Should().Be(EntityState.Unchanged);
    }

    [Fact]
    public void TheModel_HasNoCollectionWithoutAComparer_ThroughTheConventions()
    {
        using var context = new ConventionsArticleDbContext(Options<ConventionsArticleDbContext>(throwOnMissingComparer: true));

        context.Model.FindEntityType(typeof(TranslatedArticle)).Should().NotBeNull();
    }

    [Fact]
    public void TheModel_HasNoCollectionWithoutAComparer_ThroughTheModelBuilder()
    {
        using var context = new ModelBuilderArticleDbContext(Options<ModelBuilderArticleDbContext>(throwOnMissingComparer: true));

        context.Model.FindEntityType(typeof(TranslatedArticle)).Should().NotBeNull();
    }

    private LocalizedString ChangeInPlaceAndReload<TContext>(
        Func<DbContextOptions<TContext>, TContext> create, Action<LocalizedString> change)
        where TContext : DbContext
    {
        // The warning is not turned into an error here: these tests measure the save, the two above measure the warning.
        var options = Options<TContext>(throwOnMissingComparer: false);

        using (var seed = create(options))
        {
            seed.Database.EnsureCreated();
            seed.Set<TranslatedArticle>().Add(new TranslatedArticle
            {
                Title = LocalizedString.From(("en", "Welcome"), ("fr", "Bienvenue"))
            });
            seed.SaveChanges();
        }

        using (var edit = create(options))
        {
            var article = edit.Set<TranslatedArticle>().Single();
            change(article.Title);
            edit.SaveChanges();
        }

        using var read = create(options);
        return read.Set<TranslatedArticle>().Single().Title;
    }

    private DbContextOptions<TContext> Options<TContext>(bool throwOnMissingComparer) where TContext : DbContext
    {
        var builder = new DbContextOptionsBuilder<TContext>().UseSqlite(_connection);
        if (throwOnMissingComparer)
            builder.ConfigureWarnings(warnings => warnings.Throw(CoreEventId.CollectionWithoutComparer));
        return builder.Options;
    }
}

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>
///     <c>ApplyPragmaticInternationalization()</c> maps a <see cref="LocalizedString" /> property nobody declared, as the module's documentation
///     shows it used: <c>modelBuilder.Entity&lt;T&gt;()</c> and the call, nothing else.
/// </summary>
/// <remarks>
///     ⚠️ The method configured only the scalar properties it found. EF Core's conventions take an undeclared <c>LocalizedString</c> for an
///     entity type, so it was not one, and the model failed with "The entity type 'LocalizedString' requires a primary key to be defined".
/// </remarks>
public sealed class ALocalizedStringNobodyDeclaredIsMappedTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ALocalizedStringNobodyDeclaredIsMappedTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    [Fact]
    public void TheModel_MapsEachLocalizedStringAsAJsonColumn()
    {
        using var context = new UndeclaredArticleDbContext(Options());

        var article = context.Model.FindEntityType(typeof(UndeclaredArticle))!;
        article.FindProperty(nameof(UndeclaredArticle.Title))!.GetValueConverter().Should().BeOfType<LocalizedStringValueConverter>();
        article.FindProperty(nameof(UndeclaredArticle.Summary))!.GetValueConverter().Should().BeOfType<LocalizedStringValueConverter>();
        context.Model.FindEntityType(typeof(LocalizedString)).Should().BeNull("a LocalizedString is a column, not an entity");
    }

    [Fact]
    public void AValue_RoundTrips()
    {
        var options = Options();
        using (var seed = new UndeclaredArticleDbContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Articles.Add(new UndeclaredArticle { Title = LocalizedString.From(("en", "Welcome"), ("it", "Benvenuti")) });
            seed.SaveChanges();
        }

        using var read = new UndeclaredArticleDbContext(options);
        var stored = read.Articles.Single();
        stored.Title.GetExact("en").Should().Be("Welcome");
        stored.Title.GetExact("it").Should().Be("Benvenuti");
        stored.Summary.Should().BeNull();
    }

    /// <summary>The comparer is set too: without it a change made in place would not be saved (#623).</summary>
    [Fact]
    public void AChangeInPlace_IsSaved()
    {
        var options = Options();
        using (var seed = new UndeclaredArticleDbContext(options))
        {
            seed.Database.EnsureCreated();
            seed.Articles.Add(new UndeclaredArticle { Title = LocalizedString.From(("en", "Welcome")) });
            seed.SaveChanges();
        }

        using (var edit = new UndeclaredArticleDbContext(options))
        {
            edit.Articles.Single().Title.Set("it", "Benvenuti");
            edit.SaveChanges();
        }

        using var read = new UndeclaredArticleDbContext(options);
        read.Articles.Single().Title.GetExact("it").Should().Be("Benvenuti");
    }

    private DbContextOptions<UndeclaredArticleDbContext> Options()
        => new DbContextOptionsBuilder<UndeclaredArticleDbContext>().UseSqlite(_connection)
            .ConfigureWarnings(warnings => warnings.Throw(CoreEventId.CollectionWithoutComparer)).Options;
}

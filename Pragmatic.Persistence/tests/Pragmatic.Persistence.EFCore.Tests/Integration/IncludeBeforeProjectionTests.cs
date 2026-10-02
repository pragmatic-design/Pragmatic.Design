using Microsoft.EntityFrameworkCore;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.EFCore.Tests.Integration;

/// <summary>
///     What EF Core does with an <c>Include</c> that sits in front of a type-changing <c>Select</c>.
/// </summary>
/// <remarks>
///     <para>
///         The generated <c>{Entity}DtoQueryExtensions.As{Dto}()</c> is the projection alone —
///         <c>query.Select({Dto}.Projection)</c>, no include in front of it — and the include a DTO's
///         <c>RequiredNavigations</c> asks for is applied where the entity itself comes back. This is
///         the measurement that decides it, against a real provider: an include in front of a
///         type-changing <c>Select</c> changes nothing about the result, and an include where the
///         entity survives is what fills the navigation.
///     </para>
///     <para>
///         SQLite rather than the in-memory provider: an include is a JOIN, and the in-memory provider
///         does not produce SQL, so it cannot answer a question about what SQL comes out.
///     </para>
/// </remarks>
public sealed class IncludeBeforeProjectionTests
{
    private sealed record ProductRow(string Name, string? CategoryName);

    // SQLite :memory: lives as long as the open connection the DbContext holds.
    private static TestDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }

    private static async Task SeedAsync(TestDbContext db)
    {
        var category = new TestCategory { PersistenceId = Guid.NewGuid(), Name = "Tools" };
        db.Add(category);
        db.Add(new TestProduct
        {
            PersistenceId = Guid.NewGuid(),
            Name = "Hammer",
            Price = 9.99m,
            CategoryId = category.PersistenceId,
        });
        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    ///     A projection reaches the navigation on its own — that is what makes it a JOIN.
    /// </summary>
    [Fact]
    public async Task AProjectionReachesTheNavigationWithoutAnInclude()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        db.ChangeTracker.Clear();

        var rows = await db.Products
            .Select(p => new ProductRow(p.Name, p.Category!.Name))
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].CategoryName.Should().Be("Tools");
    }

    /// <summary>
    ///     And an <c>Include</c> in front of it changes nothing about the result.
    /// </summary>
    /// <remarks>
    ///     This is the measurement the decision rests on: the call is <b>useless, not harmful</b>. EF
    ///     drops an include whose entity does not survive into the result rather than refusing the
    ///     query, so <c>As{Dto}()</c> has been returning correct rows all along — which is also why
    ///     nothing ever reported it.
    /// </remarks>
    [Fact]
    public async Task AnIncludeInFrontOfItChangesNothing()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        db.ChangeTracker.Clear();

        var rows = await db.Products
            .Include(p => p.Category)
            .Select(p => new ProductRow(p.Name, p.Category!.Name))
            .ToListAsync();

        rows.Should().HaveCount(1);
        rows[0].CategoryName.Should().Be("Tools");
    }

    /// <summary>
    ///     Where an include does decide the outcome: the entity itself comes back, navigation and all.
    /// </summary>
    [Fact]
    public async Task WithoutAProjection_TheIncludeIsWhatFillsTheNavigation()
    {
        using var db = CreateContext();
        await SeedAsync(db);
        db.ChangeTracker.Clear();

        var withoutInclude = await db.Products.AsNoTracking()
            .FirstAsync();
        withoutInclude.Category.Should().BeNull();

        db.ChangeTracker.Clear();

        var withInclude = await db.Products.AsNoTracking()
            .Include(p => p.Category)
            .FirstAsync();
        withInclude.Category.Should().NotBeNull();
        withInclude.Category!.Name.Should().Be("Tools");
    }
}

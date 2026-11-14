using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     A committed write to a <c>[Lookup]</c> entity reloads its cache (#734): the cache was loaded at startup and by no write, so a
///     category added while the host ran was unknown to every lookup navigation until a restart.
/// </summary>
/// <remarks>
///     <para>
///         Written through the boundary's context and <c>SaveChanges</c>, the save every mutation's unit of work ends in: the Showcase
///         has no mutation that writes a <see cref="Category" />. The reload is the context's interceptor, so the save is what it hears.
///     </para>
///     <para>
///         The transaction cases are the control: a reload at the save would read, through a context of its own, a row its transaction
///         has not committed, and a rolled-back row must never reach the cache.
///     </para>
/// </remarks>
public class ALookupWriteReloadsTheCacheTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>A host of its own: these cases add categories, and <see cref="LookupCacheTests" /> measures a cold cache.</summary>
    protected override bool NeedsItsOwnHost => true;

    private ILookupCache<Category, Guid> Cache => Services.GetRequiredService<ILookupCache<Category, Guid>>();

    [Fact]
    public async Task ACategoryAddedWhileTheHostRuns_IsInTheCacheAfterTheSave()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        var id = Add(db, "Glamping");

        await db.SaveChangesAsync();

        Cache.TryGet(id, out var cached).Should().BeTrue("the save committed the row, and the cache is reloaded after a commit");
        cached!.Name.Should().StartWith("Glamping");
        LookupResolver.Get<Category, Guid>(id).Should().BeSameAs(cached, "and the navigations read the same cache");
    }

    [Fact]
    public async Task ACategoryAddedInATransaction_IsInTheCacheOnceItCommits()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        // The host's retrying strategy runs a user transaction as one retriable unit, as the unit of work does.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var id = Add(db, "Agriturismo");

            await db.SaveChangesAsync();
            Cache.TryGet(id, out _).Should().BeFalse("the row is not committed: a reload now would not see it");

            await transaction.CommitAsync();
            Cache.TryGet(id, out var cached).Should().BeTrue("the commit reloads the cache");
            cached!.Name.Should().StartWith("Agriturismo");
        });
    }

    [Fact]
    public async Task ACategoryRolledBack_NeverReachesTheCache()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        var id = Guid.Empty;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            id = Add(db, "Rolled back");
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        });

        // A later committed write of another category reloads the cache from the table: the rolled-back row is not in it.
        using var later = Services.CreateScope();
        var other = later.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
        var committed = Add(other, "Masseria");
        await other.SaveChangesAsync();

        Cache.TryGet(committed, out _).Should().BeTrue();
        Cache.TryGet(id, out _).Should().BeFalse("a rolled-back row is in no table, so in no cache");
    }

    private static Guid Add(DbContext db, string name)
    {
        var category = new Category();
        db.Set<Category>().Add(category);
        db.Entry(category).Property(nameof(Category.Name)).CurrentValue = $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];
        return category.PersistenceId;
    }
}

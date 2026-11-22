using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Repository;
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
///         Written through the boundary's repository and unit of work, the save every mutation ends in: the Showcase has no mutation that
///         writes a <see cref="Category" />. The reload is a commit observer every engine calls (#881), so the same file runs on EF Core,
///         Sql and documents.
///     </para>
///     <para>
///         The transaction cases are the control: a reload at the save would read, through a scope of its own, a row its transaction has
///         not committed, and a rolled-back row must never reach the cache.
///     </para>
/// </remarks>
public class ALookupWriteReloadsTheCacheTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>A host of its own: these cases add categories, and <c>LookupCacheTests</c> measures a cold cache.</summary>
    protected override bool NeedsItsOwnHost => true;

    private ILookupCache<Category, Guid> Cache => Services.GetRequiredService<ILookupCache<Category, Guid>>();

    [Fact]
    public async Task ACategoryAddedWhileTheHostRuns_IsInTheCacheAfterTheSave()
    {
        using var scope = Services.CreateScope();
        var id = Add(scope, "Glamping");

        await UnitOfWork(scope).SaveChangesAsync();

        Cache.TryGet(id, out var cached).Should().BeTrue("the save committed the row, and the cache is reloaded after a commit");
        cached!.Name.Should().StartWith("Glamping");
        LookupResolver.Get<Category, Guid>(id).Should().BeSameAs(cached, "and the navigations read the same cache");
    }

    [Fact]
    public async Task ACategoryAddedInATransaction_IsInTheCacheOnceItCommits()
    {
        using var scope = Services.CreateScope();
        var unitOfWork = UnitOfWork(scope);

        // Inside ExecuteAsync: a retrying strategy runs a user transaction as one retriable unit (IUnitOfWork.BeginTransactionAsync).
        await unitOfWork.ExecuteAsync(async ct =>
        {
            await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
            var id = Add(scope, "Agriturismo");

            await unitOfWork.SaveChangesAsync(ct);
            Cache.TryGet(id, out _).Should().BeFalse("the row is not committed: a reload now would not see it");

            await transaction.CommitAsync(ct);
            Cache.TryGet(id, out var cached).Should().BeTrue("the commit reloads the cache");
            cached!.Name.Should().StartWith("Agriturismo");
            return true;
        });
    }

    [Fact]
    public async Task ACategoryRolledBack_NeverReachesTheCache()
    {
        var id = Guid.Empty;
        using (var scope = Services.CreateScope())
        {
            var unitOfWork = UnitOfWork(scope);
            await unitOfWork.ExecuteAsync(async ct =>
            {
                await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
                id = Add(scope, "Rolled back");
                await unitOfWork.SaveChangesAsync(ct);
                await transaction.RollbackAsync(ct);
                return true;
            });
        }

        // A later committed write of another category reloads the cache from the table: the rolled-back row is not in it.
        using var later = Services.CreateScope();
        var committed = Add(later, "Masseria");
        await UnitOfWork(later).SaveChangesAsync();

        Cache.TryGet(committed, out _).Should().BeTrue();
        Cache.TryGet(id, out _).Should().BeFalse("a rolled-back row is in no table, so in no cache");
    }

    private static IUnitOfWork UnitOfWork(IServiceScope scope)
        => scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary));

    private static Guid Add(IServiceScope scope, string name)
    {
        var category = Category.Create();
        category.SetName($"{name} {Guid.NewGuid():N}"[..(name.Length + 9)]);
        scope.ServiceProvider.GetRequiredService<IRepository<Category>>().Add(category);
        return category.PersistenceId;
    }
}

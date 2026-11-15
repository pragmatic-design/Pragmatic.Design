using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Xunit;

namespace Showcase.Host.Distributed.Tests;

/// <summary>
///     A committed write to a <c>[Lookup]</c> entity on one host reloads the lookup cache of the other hosts that share the invalidation
///     broadcast (#763). After #734 it reloaded the writer's cache only, and every other instance kept the rows it loaded until it restarted.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Asserted on each host's own <see cref="ILookupCache{T, TId}" />, not through a lookup navigation. The navigations read
///         <see cref="LookupResolver" />, which is static: the three hosts of this fixture are one test process, so they share it, and a
///         navigation on the first host would read whichever cache was registered last, the writer's included. In a deployment each host
///         is its own process, and its navigations read the cache asserted here, which its loader registers with the resolver.
///     </para>
///     <para>
///         The host with no broadcast is the control: it loaded the same table at startup and hears nothing, so it does not know the row.
///         Without it, "the first host knows the row" would also be met by a cache that reloads on its own.
///     </para>
/// </remarks>
[Collection(SharedCacheCollection.Name)]
public sealed class ALookupWriteReachesTheOtherHostTests(SharedCacheFixture fixture)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task A_category_written_on_one_host_is_in_the_other_hosts_lookup_cache()
    {
        Guid id;
        using (var scope = fixture.Second.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
            id = Add(db, "Ryokan");
            await db.SaveChangesAsync();
        }

        CacheOf(fixture.Second).TryGet(id, out _).Should().BeTrue("the writer reloads its own cache after the commit, as after #734");

        (await EventuallyCachedAsync(fixture.First, id)).Should().BeTrue(
            "the writer's reload invalidated the lookup's tag, the broadcast carried it, and this host's observer reloaded the lookup");

        CacheOf(fixture.Alone).TryGet(id, out _).Should().BeFalse(
            "the control declares no broadcast: it keeps the rows it loaded at startup, which is what every other instance did before");
    }

    private static ILookupCache<Category, Guid> CacheOf(WebApplicationFactory<Program> host)
        => host.Services.GetRequiredService<ILookupCache<Category, Guid>>();

    /// <summary>
    ///     Waits for the other host's reload. The broadcast is pub/sub, so it does not arrive at once, but never arriving is a failure, and
    ///     the timeout says that.
    /// </summary>
    private static async Task<bool> EventuallyCachedAsync(WebApplicationFactory<Program> host, Guid id)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (CacheOf(host).TryGet(id, out _))
                return true;

            await Task.Delay(250);
        }

        return false;
    }

    private static Guid Add(DbContext db, string name)
    {
        var category = new Category();
        db.Set<Category>().Add(category);
        db.Entry(category).Property(nameof(Category.Name)).CurrentValue = $"{name} {Guid.NewGuid():N}"[..(name.Length + 9)];
        return category.PersistenceId;
    }
}

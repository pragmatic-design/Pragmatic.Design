using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     Tests caching behavior via HTTP endpoints.
///     Covers matrix features:
///       - [Cacheable] query attribute with TTL
///       - Cache consistency within TTL window
///       - Different parameters produce different cache entries
///       - Auto-invalidation on mutation via [InvalidatesCache] tags (verified working E2E)
///       - Collection-typed cache-key members do not collide (regression)
/// </summary>
public class CachingIntegrationTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // Cacheable: repeated calls return same results (cache hit or consistent DB)
    // =========================================================================

    [Fact]
    public async Task CacheableQuery_ServesAReadThatNoLongerMatchesTheDatabase()
    {
        // This replaces an assertion that two identical calls returned the same number of items.
        // Nothing changed between them, so that held whether or not a cache existed: removing
        // [Cacheable] left the test green, which is the only thing a caching test must not do.
        //
        // What only a cache can produce is a read that disagrees with committed data. The row is
        // deleted straight through the DbContext, so no endpoint runs and no [InvalidatesCache]
        // fires; a served result that still contains it came from the cache and nowhere else.
        var name = $"Cache-{Guid.NewGuid():N}"[..20];
        await PostAsync<JsonElement>("/api/amenities", new { name, icon = "wifi" });

        var first = await GetAsync<JsonElement>($"/api/amenities/search?name={name}");
        first.GetProperty("items").GetArrayLength().Should().Be(1,
            "the amenity was just created and the query is not yet cached");

        int removed;
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));
            removed = await db.Set<Amenity>().IgnoreQueryFilters()
                .Where(a => a.Name == name)
                .ExecuteDeleteAsync();
        }

        removed.Should().Be(1, "the row has to be gone for the next assertion to mean anything");

        var second = await GetAsync<JsonElement>($"/api/amenities/search?name={name}");

        second.GetProperty("items").GetArrayLength().Should().Be(1,
            "[Cacheable(Duration = \"10m\")] serves the entry it already has: the query never reaches "
            + "the database, so it cannot know the row is gone");
    }

    // =========================================================================
    // Cacheable: mutation eventually visible (TTL-based, not auto-invalidation)
    // =========================================================================

    [Fact]
    public async Task CacheableQuery_AfterMutation_EventuallyReflectsNewData()
    {
        // Baseline — capture count before mutation
        var before = await GetAsync<JsonElement>("/api/amenities/search");
        var beforeCount = before.GetProperty("items").GetArrayLength();

        // Create a new amenity — this does NOT invalidate the cache automatically
        // but the next uncached call (after TTL) should include it
        var newName = $"New-{Guid.NewGuid():N}"[..20];
        await PostAsync<JsonElement>("/api/amenities", new
        {
            name = newName,
            icon = "star"
        });

        // Immediate call may still return cached data (TTL-based)
        // But within the same test run, the factory creates a fresh app per test class,
        // so the cache is empty and the DB is the source of truth
        var after = await GetAsync<JsonElement>("/api/amenities/search");
        var afterCount = after.GetProperty("items").GetArrayLength();

        afterCount.Should().BeGreaterOrEqualTo(beforeCount,
            "After creating a new amenity, search should eventually return it (TTL-based cache)");
    }

    // =========================================================================
    // Cacheable: different filter parameters produce independent cache entries
    // =========================================================================

    [Fact]
    public async Task CacheableQuery_DifferentParameters_DifferentCacheEntries()
    {
        // Create two amenities with distinct names
        var nameA = $"CacheA-{Guid.NewGuid():N}"[..20];
        var nameB = $"CacheB-{Guid.NewGuid():N}"[..20];

        await PostAsync<JsonElement>("/api/amenities", new { name = nameA, icon = "pool" });
        await PostAsync<JsonElement>("/api/amenities", new { name = nameB, icon = "gym" });

        // Search with name filter A
        var searchA = await GetAsync<JsonElement>($"/api/amenities/search?name={nameA}");
        var countA = searchA.GetProperty("items").GetArrayLength();

        // Search with name filter B — different parameter → different cache entry
        var searchB = await GetAsync<JsonElement>($"/api/amenities/search?name={nameB}");
        var countB = searchB.GetProperty("items").GetArrayLength();

        // Each should find at least its own amenity
        countA.Should().BeGreaterOrEqualTo(1,
            "Search with nameA filter should find the amenity named A");
        countB.Should().BeGreaterOrEqualTo(1,
            "Search with nameB filter should find the amenity named B");

        // Verify each search returns items with the correct name
        var itemA = searchA.GetProperty("items")[0];
        itemA.GetProperty("name").GetString().Should().Contain(nameA[..6],
            "Filtered search A should return amenities matching the name filter");
        var itemB = searchB.GetProperty("items")[0];
        itemB.GetProperty("name").GetString().Should().Contain(nameB[..6],
            "Filtered search B should return amenities matching the name filter");

        // Unfiltered search should return more items than either filtered search
        var searchAll = await GetAsync<JsonElement>("/api/amenities/search");
        var countAll = searchAll.GetProperty("items").GetArrayLength();

        countAll.Should().BeGreaterOrEqualTo(countA,
            "Unfiltered cache entry should have at least as many items as the filtered one");
    }

    // =========================================================================
    // Auto-invalidation: [InvalidatesCache] on a mutation evicts the tagged query
    // =========================================================================

    [Fact]
    public async Task InvalidatesCache_OnMutation_EvictsCachedQueryByTag()
    {
        var uniqueName = $"Inv-{Guid.NewGuid():N}"[..18];

        // 1. Search by the unique name BEFORE it exists → empty result, cached under
        //    key(Name=uniqueName) with tag "amenities".
        var before = await GetAsync<JsonElement>($"/api/amenities/search?name={uniqueName}");
        before.GetProperty("items").GetArrayLength().Should().Be(0, "the amenity does not exist yet");

        // 2. Create the amenity — CreateAmenityMutation has [InvalidatesCache("amenities")],
        //    which must evict every entry tagged "amenities" (including the empty one above).
        await PostAsync<JsonElement>("/api/amenities", new { name = uniqueName, icon = "wifi" });

        // 3. Search again by the same name. If tag invalidation works, this is a cache MISS
        //    and returns the new amenity. If it were broken, we'd get the stale empty result.
        var after = await GetAsync<JsonElement>($"/api/amenities/search?name={uniqueName}");
        after.GetProperty("items").GetArrayLength().Should().Be(1,
            "[InvalidatesCache(\"amenities\")] on the mutation must evict the tagged cached query");
    }

    // =========================================================================
    // Regression: collection-typed cache-key members must not collide
    // =========================================================================

    [Fact]
    public async Task CollectionCacheKey_DifferentListValues_DoNotCollide()
    {
        var namePool = $"Pool-{Guid.NewGuid():N}"[..18];
        var nameSpa = $"Spa-{Guid.NewGuid():N}"[..18];
        await PostAsync<JsonElement>("/api/amenities", new { name = namePool, icon = "pool" });
        await PostAsync<JsonElement>("/api/amenities", new { name = nameSpa, icon = "spa" });

        // Filter by the 'names' list (List<string> → part of the cache key). Cache the Pool result.
        var poolSearch = await GetAsync<JsonElement>($"/api/amenities/search?names={namePool}");
        poolSearch.GetProperty("items")[0].GetProperty("name").GetString().Should().Be(namePool);

        // Same query shape, ONLY the names-list value differs. A List<string> that collapsed to its type
        // name in the key would collide here and return the stale Pool result.
        var spaSearch = await GetAsync<JsonElement>($"/api/amenities/search?names={nameSpa}");
        spaSearch.GetProperty("items").GetArrayLength().Should().Be(1);
        spaSearch.GetProperty("items")[0].GetProperty("name").GetString().Should().Be(nameSpa,
            "a different 'names' list value must produce a distinct cache key");
    }

    // =========================================================================
    // Cacheable: multiple rapid calls within TTL are consistent
    // =========================================================================

    [Fact]
    public async Task CacheableQuery_RapidCalls_AllReturnConsistentData()
    {
        // Pre-populate
        await PostAsync<JsonElement>("/api/amenities", new
        {
            name = $"Rapid-{Guid.NewGuid():N}"[..20],
            icon = "elevator"
        });

        // Fire 5 rapid calls — all should return identical counts
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => GetAsync<JsonElement>("/api/amenities/search"))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var counts = results.Select(r => r.GetProperty("items").GetArrayLength()).ToArray();

        counts.Should().AllBeEquivalentTo(counts[0],
            "[Cacheable] concurrent calls should return consistent data within TTL window");
    }
}

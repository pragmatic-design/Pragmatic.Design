using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Entity;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     What <c>[Lookup]</c> is for: rows loaded once at startup, and a navigation that resolves from
///     that cache instead of the database.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Wiring alone is not the mechanism.
///         <see cref="InfrastructureTests.LookupCache_TheDeclaredLookup_HasItsCacheAndPreloadRegistered" />
///         asserts the cache, the loader and the hosted service are in the container — all three
///         about wiring. With no <see cref="Category" /> row and nothing carrying a <c>CategoryId</c>,
///         the loader would run against an empty table and every assertion would hold with the
///         <c>Load</c> call deleted from the generated loader. A preload that loads nothing looks
///         exactly like a preload of an empty table.
///     </para>
///     <para>
///         The rows are seeded by <see cref="PostgresFixture" /> before any host boots, which is where
///         reference data comes from in production: it is in the database when the application starts,
///         not written by it. That also keeps the preload's ordering out of the test — there is no
///         hosted service to run before another one.
///     </para>
/// </remarks>
public class LookupCacheTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     This class needs a cold host: it measures the lookup cache itself.
    /// </summary>
    /// <remarks>
    ///     <c>TheGeneratedNavigation_ResolvesTheRowThroughTheCache</c> asserts that a navigation returns
    ///     <b>the cached instance</b> — a reference comparison — which only means something against a
    ///     cache this test populated. Sharing the collection's host would hand it one another test
    ///     warmed, and the assertion would be about somebody else's instance.
    /// </remarks>
    protected override bool NeedsItsOwnHost => true;

    private ILookupCache<Category, Guid> Cache =>
        Services.GetRequiredService<ILookupCache<Category, Guid>>();

    /// <summary>The preload put the rows in the cache — the claim nothing made before.</summary>
    [Fact]
    public void ThePreload_FillsTheCacheAtStartup()
    {
        var cached = Cache.GetAll();

        cached.Select(c => c.Name).Should().Contain(SeededCategories.Names,
            "the loader reads the table at startup, and the table has rows");

        Cache.Get(SeededCategories.BeachResortId).Name
            .Should().Be(SeededCategories.BeachResortName, "and it is keyed by PersistenceId");
    }

    /// <summary>
    ///     The generated navigation returns the cached row — the same instance, from an entity read
    ///     with no <c>Include</c>.
    /// </summary>
    /// <remarks>
    ///     <c>Property.Category</c> is <c>[NotMapped]</c> and reads <c>LookupResolver</c>, so this is a
    ///     statement about the cache and not about EF: an entity loaded <c>AsNoTracking</c> with no
    ///     <c>Include</c> has no way to materialise a related row, and the instance it hands back is
    ///     reference-identical to the one the cache holds.
    /// </remarks>
    [Fact]
    public async Task TheGeneratedNavigation_ResolvesTheRowThroughTheCache()
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"LKP{Guid.NewGuid():N}"[..12],
            name = "Lookup Test Property",
            city = "Rimini",
            country = "IT",
            starRating = 4,
            categoryId = SeededCategories.BeachResortId
        });

        var id = created.GetProperty("id").GetGuid();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        // IgnoreQueryFilters: outside a request there is no ambient tenant, and Property is an
        // ITenantEntity — the row is there, the filter is not the subject.
        var property = await db.Set<Property>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(p => p.PersistenceId == id);

        property.CategoryId.Should().Be(SeededCategories.BeachResortId,
            "the mutation carried the foreign key through");

        property.Category.Should().NotBeNull(
            "the navigation resolves from the cache the preload filled");
        property.Category!.Name.Should().Be(SeededCategories.BeachResortName);
        property.Category.Should().BeSameAs(Cache.Get(SeededCategories.BeachResortId),
            "it is the cached instance, not a row this query loaded");
    }

    /// <summary>The control: no foreign key, no row.</summary>
    /// <remarks>
    ///     Without it, a navigation that returned some fixed category would satisfy the case above.
    /// </remarks>
    [Fact]
    public async Task WithoutAForeignKey_TheNavigationIsNull()
    {
        var created = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"LKN{Guid.NewGuid():N}"[..12],
            name = "Uncategorised Property",
            city = "Rimini",
            country = "IT",
            starRating = 3
        });

        var id = created.GetProperty("id").GetGuid();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        var property = await db.Set<Property>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstAsync(p => p.PersistenceId == id);

        property.CategoryId.Should().BeNull();
        property.Category.Should().BeNull("a null FK resolves to nothing, not to the first cached row");
    }

    /// <summary>
    ///     And the second control: the cache answers for the ids it holds, and refuses the ones it does
    ///     not — the way <c>LookupCache.Get</c> says it does.
    /// </summary>
    /// <remarks>
    ///     This is what makes "the cache is filled" mean something. A cache that answered everything, or
    ///     that were never read at all, would leave the two cases above green.
    /// </remarks>
    [Fact]
    public void AnIdTheCacheDoesNotHold_IsRefused()
    {
        var absent = Guid.NewGuid();

        Cache.TryGet(absent, out var value).Should().BeFalse();
        value.Should().BeNull();

        var get = () => Cache.Get(absent);
        get.Should().Throw<KeyNotFoundException>(
            "a missing lookup id is an error, not a silent null");
    }
}

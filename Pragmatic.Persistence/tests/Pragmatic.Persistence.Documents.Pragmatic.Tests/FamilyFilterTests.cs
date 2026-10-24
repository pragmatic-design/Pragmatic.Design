using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     Pragmatic's filter families on documents, as the Sql engine applies them (<c>FamilyFilterTests</c> there): the tenant, the owner, the
///     scopes, the soft delete and a fixed rule, per caller and mode, on MongoDB and Cosmos DB. A tenant's entity is read in the tenant's
///     partition on Cosmos DB, so a count of it is served there.
/// </summary>
[Collection("document servers")]
public class FamilyFilterTests(DocumentServers servers)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private static async Task<string[]> NamesAsync(DocSession session) => (await session.Query<Property>().ToListAsync()).Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

    private static async Task<int[]> NightsAsync(DocSession session) => (await session.Query<Reservation>().ToListAsync()).Select(r => r.Nights).Order().ToArray();

    private static async Task<string[]> TextsAsync(DocSession session) => (await session.Query<Review>().ToListAsync()).Select(r => r.Text).Order(StringComparer.Ordinal).ToArray();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_caller_reads_the_documents_the_families_let_through(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        var anna = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));

        Assert.Equal(["RIVA"], await NamesAsync(anna)); // acme's, shared with desk-north
        Assert.Equal(new[] { 3, 4 }, await NightsAsync(anna)); // hers, not deleted
        Assert.Equal(["anna@RIVA"], await TextsAsync(anna)); // acme's, hers, published
        // In a chain's own conditions too, and as a count of a tenant's entity (in its partition on Cosmos DB).
        Assert.Equal([], (await anna.Query<Review>().Where((r, text) => r.Text == text, "bruno@RIVA").ToListAsync()).Select(r => r.Text));
        Assert.Equal(1L, await anna.Query<Review>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_modes_and_the_lifts_open_the_families_as_Pragmatic_decides(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        var anna = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));

        using (_toggle.UseMode(FilterMode.Admin)) // the permission families lifted; the tenant, the soft delete and the rule kept
        {
            Assert.Equal(["MARE", "RIVA"], await NamesAsync(anna));
            Assert.Equal(new[] { 2, 3, 4 }, await NightsAsync(anna));
            Assert.Equal(["anna@RIVA", "bruno@RIVA"], await TextsAsync(anna));
        }

        using (_toggle.DisableVisibilityRule<PublishedReviews>())
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["anna draft", "anna@RIVA", "bruno@RIVA"], await TextsAsync(anna)); // and the rule by its name

        using (_toggle.UseMode(FilterMode.Raw))
        {
            Assert.Equal(["ALPE", "MARE", "RIVA"], await NamesAsync(anna));
            Assert.Equal(new[] { 2, 3, 4, 9 }, await NightsAsync(anna));
        }

        using (_toggle.Disable<Reservation.OwnershipFilter>())
            Assert.Equal(new[] { 2, 3, 4 }, await NightsAsync(anna)); // one filter lifted by its type

        var viewAll = DocumentServers.As(seeded, DocumentRows.Anna(_toggle, ["reservation.view-all"]));
        Assert.Equal(new[] { 2, 3, 4 }, await NightsAsync(viewAll)); // the bypass permission

        var anonymous = DocumentServers.As(seeded, new PragmaticDocFilters(null, new TenantScope(), new TestScopes("desk-north"), _toggle));
        Assert.Empty(await NightsAsync(anonymous)); // fail closed
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_join_reads_both_sides_within_the_families(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        var anna = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));

        // Her live stays (RIVA 3, MARE 4) with the properties she may see (RIVA: MARE is desk-south's): the joined side's families hold too.
        var joined = await anna.Query<Reservation>().Join(anna.Query<Property>(), r => r.PropertyId, p => p.PersistenceId, (r, p) => p.Name + ":" + r.Nights).ToListAsync();
        Assert.Equal(["RIVA:3"], joined);
    }

    [Fact]
    public async Task On_MongoDB_the_aggregates_and_the_commands_read_the_callers_documents()
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, Store.Mongo);
        var anna = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));

        Assert.Equal(7, await anna.Query<Reservation>().SumAsync(r => r.Nights)); // 3 + 4: hers, live
        Assert.Equal(2L, await anna.Query<Reservation>().CountAsync());
        Assert.Equal(2L, await anna.Query<Reservation>().ExecuteUpdateAsync(s => s.Set(r => r.Nights, 1))); // a command touches hers only
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(new[] { 1, 1, 2, 9 }, await NightsAsync(anna));
    }
}

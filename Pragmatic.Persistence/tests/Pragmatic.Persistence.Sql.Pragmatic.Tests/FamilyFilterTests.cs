using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Persistence.Sql.Pragmatic;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The filter families of Pragmatic's entities on the five databases, in each position a statement reads rows, decided per caller and mode: the
///     tenant (Property), the scopes (Property), the owner (Reservation, Review), the soft delete (RoomType), a fixed rule (Review). The caller
///     is anna, of the tenant acme, holding the scope desk-north, unless a test says otherwise.
/// </summary>
/// <remarks>
///     The rows: RIVA (acme, desk-north), MARE (acme, desk-south), ALPE (other, desk-north). Reservations: anna's at RIVA, MARE and ALPE,
///     bruno's at RIVA. Reviews, published unless said: anna's at RIVA and MARE, bruno's at RIVA, anna's unpublished at ALPE. Room types at
///     RIVA: STD, and SUI deleted.
/// </remarks>
[Collection("pragmatic databases")]
public class FamilyFilterTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle(); // the interface: DisableVisibilityRule is one of its default members

    private async Task<SqlSession> Seeded(SqlDialect dialect, ICurrentUser? user = null, QueryFilterOptions? options = null)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(user ?? new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, options));
        await PragmaticRows.SeedAsync(session, _toggle);
        return session;
    }

    private static Task<List<string>> Properties(SqlSession session) => session.Query<Property>().OrderBy(p => p.Name).Select(p => p.Name).ToListAsync().AsTask();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task At_the_root_each_family_keeps_the_callers_rows(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        Assert.Equal(["RIVA"], await Properties(session)); // the tenant's, sharing a scope
        Assert.Equal(3, await session.Query<Reservation>().CountAsync()); // anna's
        Assert.Equal(["anna@MARE", "anna@RIVA"], await session.Query<Review>().OrderBy(r => r.Text).Select(r => r.Text).ToListAsync()); // anna's, published
        Assert.Equal(1, await session.Query<RoomType>().CountAsync()); // the live one
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task Each_mode_lifts_what_it_lifts_and_the_variants_do_not_mix(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        using (_toggle.UseMode(FilterMode.Admin))
        {
            Assert.Equal(["MARE", "RIVA"], await Properties(session)); // scopes lifted, the tenant kept
            Assert.Equal(4, await session.Query<Reservation>().CountAsync()); // every owner's
            Assert.Equal(1, await session.Query<RoomType>().CountAsync()); // the soft delete kept
        }

        using (_toggle.UseMode(FilterMode.Background))
            Assert.Equal(["ALPE", "MARE", "RIVA"], await Properties(session)); // the tenant lifted too

        using (_toggle.UseMode(FilterMode.Raw))
        {
            Assert.Equal(2, await session.Query<RoomType>().CountAsync()); // nothing kept
            Assert.Equal(4, await session.Query<Review>().CountAsync()); // the rule lifted with the rest
        }

        Assert.Equal(["RIVA"], await Properties(session)); // the same plan, back to the caller's variant
        Assert.Equal(3, await session.Query<Reservation>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_bypass_permission_the_anonymous_caller_and_the_lifts_by_type_and_name(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using (var viewAll = await Seeded(dialect, new TestUser("anna", ["property.view-all"])))
            Assert.Equal(["MARE", "RIVA"], await Properties(viewAll)); // past the scopes, not the tenant

        using (var anonymous = await Seeded(dialect, AnonymousUser.Instance))
        {
            Assert.Equal(0, await anonymous.Query<Reservation>().CountAsync()); // fails closed
            Assert.Empty(await Properties(anonymous));
        }

        using (var open = await Seeded(dialect, AnonymousUser.Instance, new QueryFilterOptions { FailClosedWhenAnonymous = false }))
            Assert.Equal(4, await open.Query<Reservation>().CountAsync());

        using var session = await Seeded(dialect);
        using (_toggle.Disable<Reservation.OwnershipFilter>())
            Assert.Equal(4, await session.Query<Reservation>().CountAsync());
        using (_toggle.Disable<RoomType.SoftDeleteFilter>())
        {
            Assert.Equal(1, await session.Query<RoomType>().CountAsync()); // the provider's half alone: EF's named filter still applies
            using (_toggle.DisableQueryFilter("SoftDelete"))
                Assert.Equal(2, await session.Query<RoomType>().CountAsync());
        }

        using (_toggle.DisableVisibilityRule<PublishedReviews>())
            Assert.Equal(3, await session.Query<Review>().CountAsync()); // anna's, the unpublished one too
    }

    [Fact]
    public async Task The_runtime_path_refuses_a_Pragmatic_entity_rather_than_read_it_unfiltered()
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(SqlDialect.Sqlite);
        IQueryable<Review> dynamic = session.Query<Review>(); // an IQueryable: the runtime path, which has no families yet
        var refused = await Assert.ThrowsAsync<NotSupportedException>(async () => await dynamic.Where(r => r.Text != "").ToListAsync());
        Assert.Contains("Review is a Pragmatic entity", refused.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_set_based_command_touches_the_callers_rows_only(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        Assert.Equal(2, await session.Query<Review>().ExecuteUpdateAsync(s => s.SetProperty(r => r.Text, "edited"))); // anna's published ones
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(2, await session.Query<Review>().CountAsync((r, text) => r.Text != text, "edited")); // bruno's and anna's unpublished one, untouched
        Assert.Equal(2, await session.Query<Review>().ExecuteDeleteAsync()); // the same two
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(2, await session.Query<Review>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task On_a_navigations_join_the_target_is_filtered_too(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        // anna's stays, at the properties she may see: MARE (another scope) and ALPE (another tenant) drop with their stays. On EF, a
        // reference is not visited by the provider's filters (FilterScope.cs:20-21): ClosesEfGap.
        Assert.Equal(["RIVA"], await session.Query<Reservation>().Select(r => r.Property.Name).ToListAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task In_a_collections_subquery_and_a_written_one_the_children_are_filtered(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        // Every property (the root's filters left out), with a review anna may read: her published ones, at RIVA and MARE.
        var withReviews = await session.Query<Property>().WithoutFilters().Where(p => p.Reviews.Any()).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();
        Assert.Equal(["MARE", "RIVA"], withReviews);
        var written = await session.Query<Property>().WithoutFilters().Where(p => session.Query<Review>().Any(r => r.PropertyId == p.Id)).OrderBy(p => p.Name).Select(p => p.Name).ToListAsync();
        Assert.Equal(["MARE", "RIVA"], written);
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task An_include_reads_the_children_the_caller_may_see(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        var riva = await session.Query<Property>().Include(p => p.Reviews).Include(p => p.RoomTypes).FirstOrDefaultAsync();
        Assert.Equal(["anna@RIVA"], riva!.Reviews.Select(r => r.Text));
        Assert.Equal(["STD"], riva.RoomTypes.Select(t => t.Code));
        // The root's WithoutFilters() reads past its includes' filters too, as past its joins'.
        var all = await session.Query<Property>().WithoutFilters().Include(p => p.Reviews).OrderBy(p => p.Name).ToListAsync();
        Assert.Equal([1, 1, 2], all.Select(p => p.Reviews.Count)); // ALPE, MARE, RIVA: every review, published or not, anyone's
    }
}

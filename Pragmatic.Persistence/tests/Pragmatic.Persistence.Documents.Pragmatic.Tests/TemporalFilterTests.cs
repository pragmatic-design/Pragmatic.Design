using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Documents.Pragmatic;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     <c>[TemporalRelation]</c>'s filter on MongoDB and Cosmos DB, as on the Sql engine (<c>TemporalFilterTests</c> there): the stretches active
///     at the caller's instant, a mode keeps them, <c>IncludeHistory</c> and Raw lift the filter. On Cosmos DB the assignments are partitioned
///     by their id, so the list is merged and the count summed across partitions.
/// </summary>
[Collection("document servers")]
public class TemporalFilterTests(DocumentServers servers)
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();
    private readonly MovingClock _clock = new(T);

    private async Task<DocSession> Seeded(Store store)
    {
        var seeding = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await seeding.EnsureContainerAsync<StaffAssignment>();
            await seeding.EnsureContainerAsync<Property>();
        }

        var riva = new Property { Name = "RIVA", TenantId = "acme" };
        riva.GrantScope("desk-north");
        await seeding.InsertAsync(riva);
        var assignments = new List<StaffAssignment>();
        foreach (var (person, from, to) in new (string, TimeSpan, TimeSpan?)[]
                 {
                     ("past", TimeSpan.FromDays(-10), TimeSpan.FromDays(-1)), ("current", TimeSpan.FromDays(-2), null),
                     ("ending", TimeSpan.FromDays(-2), TimeSpan.FromDays(1)), ("future", TimeSpan.FromDays(1), null),
                 })
        {
            var assignment = new StaffAssignment { Person = person, ValidFrom = T + from, ValidTo = to is { } end ? T + end : null };
            assignment.SetPropertyId(riva.Id);
            assignments.Add(assignment);
        }

        await seeding.InsertManyAsync(assignments);
        return DocumentServers.As(seeding, new PragmaticDocFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, timeProvider: _clock));
    }

    private static async Task<List<string>> People(DocSession session) => (await session.Query<StaffAssignment>().OrderBy(a => a.Person).ToListAsync()).Select(a => a.Person).ToList();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task The_stretches_active_at_the_callers_instant_are_read_and_a_mode_keeps_them(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        Assert.Equal(["current", "ending"], await People(session));
        Assert.Equal(2, await session.Query<StaffAssignment>().CountAsync());
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["current", "ending"], await People(session));
        using (_toggle.UseMode(FilterMode.Background))
            Assert.Equal(["current", "ending"], await People(session));

        _clock.Now = T + TimeSpan.FromDays(2);
        Assert.Equal(["current", "future"], await People(session));
    }

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task IncludeHistory_and_Raw_read_every_stretch_and_a_join_keeps_the_filter(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var session = await Seeded(store);
        using (StaffAssignment.IncludeHistory(_toggle))
            Assert.Equal(["current", "ending", "future", "past"], await People(session));
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(4, await session.Query<StaffAssignment>().CountAsync());
        // The join's inner side is filtered too: the property's active stretches only.
        var joined = await session.Query<Property>().Join(session.Query<StaffAssignment>(), p => p.PersistenceId, a => a.PropertyId, (p, a) => a.Person).ToListAsync();
        Assert.Equal(["current", "ending"], joined.Order(StringComparer.Ordinal));
    }

    /// <summary>A clock a test moves: the instant the filters read.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

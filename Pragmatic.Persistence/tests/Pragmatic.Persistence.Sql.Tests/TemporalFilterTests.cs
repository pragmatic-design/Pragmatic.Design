using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[TemporalRelation]</c>'s filter on the five databases, as Pragmatic's <c>TemporalFilter</c> decides it: the stretches active at the
///     caller's instant (<c>ValidFrom &lt;= now &amp;&amp; (ValidTo == null || ValidTo &gt; now)</c>, now from the caller's clock), in every
///     position; a plain filter, so Admin and Background keep it, <c>IncludeHistory</c> and Raw lift it.
/// </summary>
/// <remarks>
///     The assignments at RIVA, against the instant T: "past" (T-10d to T-1d), "current" (T-2d, open), "ending" (T-2d to T+1d), "future" (from
///     T+1d, open).
/// </remarks>
[Collection("pragmatic databases")]
public class TemporalFilterTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();
    private readonly MovingClock _clock = new(T);

    private async Task<SqlSession> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, timeProvider: _clock));
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var riva = new Property { Name = "RIVA", TenantId = "acme" };
            riva.GrantScope("desk-north");
            await session.InsertAsync(riva);
            foreach (var (person, from, to) in new (string, TimeSpan, TimeSpan?)[]
                     {
                         ("past", TimeSpan.FromDays(-10), TimeSpan.FromDays(-1)), ("current", TimeSpan.FromDays(-2), null),
                         ("ending", TimeSpan.FromDays(-2), TimeSpan.FromDays(1)), ("future", TimeSpan.FromDays(1), null),
                     })
            {
                var assignment = new StaffAssignment { Person = person, ValidFrom = T + from, ValidTo = to is { } end ? T + end : null };
                assignment.SetPropertyId(riva.Id);
                await session.InsertAsync(assignment);
            }
        }

        return session;
    }

    private static Task<List<string>> People(SqlSession session) => session.Query<StaffAssignment>().OrderBy(a => a.Person).Select(a => a.Person).ToListAsync().AsTask();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task The_stretches_active_at_the_callers_instant_are_read_and_a_mode_keeps_them(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        Assert.Equal(["current", "ending"], await People(session));
        Assert.Equal(2, await session.Query<StaffAssignment>().CountAsync());
        using (_toggle.UseMode(FilterMode.Admin))
            Assert.Equal(["current", "ending"], await People(session)); // not permission-based: Admin keeps it
        using (_toggle.UseMode(FilterMode.Background))
            Assert.Equal(["current", "ending"], await People(session)); // nor a tenant's: Background keeps it

        _clock.Now = T + TimeSpan.FromDays(2); // the instant is the caller's clock, read per execution
        Assert.Equal(["current", "future"], await People(session));
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task IncludeHistory_and_Raw_read_every_stretch(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        using (StaffAssignment.IncludeHistory(_toggle))
            Assert.Equal(["current", "ending", "future", "past"], await People(session));
        Assert.Equal(["current", "ending"], await People(session)); // disposed: active ones again
        using (_toggle.UseMode(FilterMode.Raw))
            Assert.Equal(4, await session.Query<StaffAssignment>().CountAsync());
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task In_a_collections_subquery_and_an_include_the_children_are_the_active_ones(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await Seeded(dialect);
        Assert.Equal(2, await session.Query<Property>().Select(p => p.StaffAssignments.Count()).FirstOrDefaultAsync());
        var riva = await session.Query<Property>().Include(p => p.StaffAssignments).FirstOrDefaultAsync();
        Assert.Equal(["current", "ending"], riva!.StaffAssignments.Select(a => a.Person).Order(StringComparer.Ordinal));
        _clock.Now = T - TimeSpan.FromDays(5);
        Assert.False(await session.Query<Property>().AnyAsync(p => p.StaffAssignments.Any(a => a.Person == "current"))); // not started yet
    }

    /// <summary>A clock a test moves: the instant the filters read.</summary>
    private sealed class MovingClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

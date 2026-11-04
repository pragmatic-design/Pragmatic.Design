using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[TemporalRelation&lt;Property&gt;(MaxActive = 2)]</c> through a generated mutation's invoker, on the five databases (#512): a third
///     stretch open at the same property is refused with the constraint's error and not written, while one at another property is written. The
///     check reads the stretches already written through the engine's repository, inside the caller's filters, as EF Core's reads them.
/// </summary>
[Collection("pragmatic databases")]
public class TemporalConstraintTests(PragmaticDatabases databases)
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();
    private readonly FixedClock _clock = new(T);

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_second_open_stretch_at_the_same_property_is_refused(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, timeProvider: _clock));
        var riva = new Property { Name = "RIVA", TenantId = "acme" };
        var mare = new Property { Name = "MARE", TenantId = "acme" };
        using (_toggle.UseMode(FilterMode.Raw))
        {
            await session.InsertAsync(riva);
            await session.InsertAsync(mare);
        }

        var unitOfWork = new SqlUnitOfWork(session, new FixedClock(T), new TestUser("anna"), new TenantScope());
        var invoker = new TakeDeskDutyMutation.Invoker(new DeskDuty.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider());

        var first = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "anna", From = T.AddHours(-3) });
        var second = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "bruno", From = T.AddHours(-2) });
        var third = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "carla", From = T.AddHours(-1) });
        var elsewhere = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = mare.Id, Person = "carla", From = T.AddHours(-1) });

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess); // two at a time
        var refused = Assert.IsType<TemporalOverlapError>(third.Error);
        Assert.Equal((TemporalViolationType.MaxActiveExceeded, 2), (refused.ViolationType, refused.CurrentActive));
        Assert.True(elsewhere.IsSuccess); // the constraint is per property
        using (_toggle.UseMode(FilterMode.Raw))
        {
            Assert.Equal(["anna", "bruno"], await session.Query<DeskDuty>().Where((d, id) => d.PropertyId == id, riva.Id).OrderBy(d => d.Person).Select(d => d.Person).ToListAsync()); // carla's refused
            Assert.Equal(["carla"], await session.Query<DeskDuty>().Where((d, id) => d.PropertyId == id, mare.Id).Select(d => d.Person).ToListAsync());
        }
    }
}

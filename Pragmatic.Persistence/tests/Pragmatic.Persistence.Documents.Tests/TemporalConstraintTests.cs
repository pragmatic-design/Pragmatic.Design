using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     <c>[TemporalRelation&lt;Property&gt;(MaxActive = 1)]</c> through a generated mutation's invoker, on MongoDB and Cosmos DB, as on the Sql
///     engine (<c>TemporalConstraintTests</c> there, #512): a second stretch open at the same property is refused with the constraint's error and
///     not written, while one at another property is written; a closed stretch is refused where it runs into an open one (no overlap allowed),
///     and written where it ends before.
/// </summary>
[Collection("document servers")]
public class TemporalConstraintTests(DocumentServers servers)
{
    private static readonly DateTimeOffset T = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_second_open_stretch_at_the_same_property_is_refused(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeding = servers.Session(store);
        if (store == Store.Cosmos)
        {
            await seeding.EnsureContainerAsync<DeskDuty>();
            await seeding.EnsureContainerAsync<Property>();
        }

        var riva = new Property { Name = "RIVA", TenantId = "acme" };
        var mare = new Property { Name = "MARE", TenantId = "acme" };
        await seeding.InsertAsync(riva);
        await seeding.InsertAsync(mare);
        var session = DocumentServers.As(seeding, new PragmaticDocFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, timeProvider: new FixedClock(T)));
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(T), new TestUser("anna"), new TenantScope());
        var invoker = new TakeDeskDutyMutation.Invoker(new DeskDuty.DocRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider());

        var first = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "anna", From = T.AddHours(-2) });
        var second = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "bruno", From = T.AddHours(-1) });
        var elsewhere = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = mare.Id, Person = "bruno", From = T.AddHours(-1) });
        // Closed stretches that start before anna's: none is active when it starts, so the overlap check decides.
        var before = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "carla", From = T.AddHours(-5), To = T.AddHours(-4) });
        var across = await invoker.InvokeAsync(new TakeDeskDutyMutation { PropertyId = riva.Id, Person = "dario", From = T.AddHours(-3), To = T.AddHours(-1) });

        Assert.True(first.IsSuccess);
        var refused = Assert.IsType<TemporalOverlapError>(second.Error);
        Assert.Equal(TemporalViolationType.MaxActiveExceeded, refused.ViolationType);
        Assert.True(elsewhere.IsSuccess); // the constraint is per property
        Assert.True(before.IsSuccess); // ends before anna's starts
        Assert.Equal(TemporalViolationType.OverlapDetected, Assert.IsType<TemporalOverlapError>(across.Error).ViolationType); // runs into anna's
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var duties = await session.Query<DeskDuty>().ToListAsync();
            Assert.Equal(["anna", "carla"], duties.Where(d => d.PropertyId == riva.Id).Select(d => d.Person).Order(StringComparer.Ordinal)); // bruno's and dario's refused
            Assert.Equal(["bruno"], duties.Where(d => d.PropertyId == mare.Id).Select(d => d.Person));
        }
    }
}

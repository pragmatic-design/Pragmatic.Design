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

    /// <summary>
    ///     <c>[TemporalRelation&lt;Property&gt;(MaxActive = 1)]</c> written through the session, past any mutation's check (#523): where the
    ///     database has partial indexes, the schema's <c>IX_KeyHolders_PropertyId_Active</c> refuses a second open stretch at one property, and
    ///     admits a closed one beside it and an open one at another property. MySQL and Oracle have no partial index: the schema leaves it out
    ///     (PRAG3039), and the second open stretch is written.
    /// </summary>
    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_second_open_key_holder_written_past_the_mutation_is_refused_by_the_database(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle, timeProvider: _clock));
        using var raw = _toggle.UseMode(FilterMode.Raw);
        var riva = new Property { Name = "RIVA", TenantId = "acme" };
        var mare = new Property { Name = "MARE", TenantId = "acme" };
        await session.InsertAsync(riva);
        await session.InsertAsync(mare);

        await session.InsertAsync(Holder(riva, "anna", T.AddHours(-3)));
        await session.InsertAsync(Holder(riva, "bruno", T.AddHours(-5), T.AddHours(-4))); // closed
        await session.InsertAsync(Holder(mare, "bruno", T.AddHours(-1))); // another property
        var second = Holder(riva, "carla", T.AddHours(-1));

        if (dialect is SqlDialect.MySql or SqlDialect.Oracle)
        {
            await session.InsertAsync(second);
            Assert.Equal(2, await session.Query<KeyHolder>().Where((k, id) => k.PropertyId == id && k.ValidTo == null, riva.Id).CountAsync());
            return;
        }

        var refused = await Assert.ThrowsAnyAsync<Exception>(async () => await session.InsertAsync(second));
        var text = refused.ToString();
        Assert.True(text.Contains("IX_KeyHolders_PropertyId_Active", StringComparison.OrdinalIgnoreCase) || text.Contains("KeyHolders.PropertyId", StringComparison.Ordinal), text);
        Assert.Equal(["anna"], await session.Query<KeyHolder>().Where((k, id) => k.PropertyId == id && k.ValidTo == null, riva.Id).Select(k => k.Person).ToListAsync());
    }

    private static KeyHolder Holder(Property property, string person, DateTimeOffset from, DateTimeOffset? to = null)
    {
        var holder = new KeyHolder { Person = person, ValidFrom = from, ValidTo = to };
        holder.SetPropertyId(property.Id);
        return holder;
    }
}

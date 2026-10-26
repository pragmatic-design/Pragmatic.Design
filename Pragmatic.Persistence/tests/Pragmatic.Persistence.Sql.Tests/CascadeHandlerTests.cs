using Pragmatic.Events;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     The cascade handler [CascadeOn] generates for a target the Sql engine persists, on the five databases: a property's region set through
///     <c>SetRegion</c> raises the change, the unit of work dispatches it after its save, and the handler copies the value onto the reservations
///     of that property through their repository. anna, in acme, holds desk-north: the reservations she reads are hers, as an EF Core
///     <c>ExecuteUpdate</c> keeps the query filters.
/// </summary>
[Collection("pragmatic databases")]
public class CascadeHandlerTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    /// <summary>The application's dispatcher, standing for Pragmatic.Events': the changes of a property go to the generated handler.</summary>
    private sealed class Dispatcher : IDomainEventDispatcher
    {
        public Func<IDomainEvent, Task> Handle { get; set; } = _ => Task.CompletedTask;

        public Task DispatchAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IDomainEvent => Handle(@event);

        public async Task DispatchAsync(IEnumerable<IDomainEvent> events, CancellationToken ct = default)
        {
            foreach (var @event in events)
                await Handle(@event);
        }
    }

    private async Task<List<(string Owner, Guid Property, string? Region)>> Reservations(SqlSession session)
    {
        using (_toggle.UseMode(FilterMode.Raw))
            return (await session.Query<Reservation>().ToListAsync()).Select(r => (r.OwnerId!, r.PropertyId, r.PropertyRegion)).ToList();
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_changed_region_reaches_the_reservations_of_its_property(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var anna = new TestUser("anna");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(anna, new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        Guid riva, mare;
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = await session.Query<Property>().ToListAsync();
            (riva, mare) = (properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "MARE").Id);
        }

        var dispatcher = new Dispatcher();
        using var unitOfWork = new SqlUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), anna, new TenantScope(), dispatcher: dispatcher);
        dispatcher.Handle = e => e is EntityPropertyChanged<Property> changed
            ? new ReservationPropertyRegionCascadeHandler(new Reservation.SqlRepository(unitOfWork), unitOfWork).HandleAsync(changed, CancellationToken.None)
            : Task.CompletedTask;

        var property = (await new Property.SqlRepository(unitOfWork).GetByIdAsync(riva))!;
        property.SetRegion("north");
        await unitOfWork.SaveChangesAsync();

        var reservations = await Reservations(session);
        Assert.Equal("north", reservations.Single(r => r is { Owner: "anna" } && r.Property == riva).Region);
        Assert.Null(reservations.Single(r => r is { Owner: "bruno" } && r.Property == riva).Region); // not anna's to read
        Assert.Null(reservations.Single(r => r is { Owner: "anna" } && r.Property == mare).Region); // another property
    }
}

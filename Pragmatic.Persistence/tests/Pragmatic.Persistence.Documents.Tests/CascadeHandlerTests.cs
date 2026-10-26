using Pragmatic.Events;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents;
using Pragmatic.Persistence.Query.Filters;
using PragmaticEntities.Tests;
using Xunit;

namespace PragmaticDocuments.Tests;

/// <summary>
///     The cascade handler [CascadeOn] generates for a target the documents engine persists, on MongoDB and Cosmos DB, as on the Sql engine
///     (<c>CascadeHandlerTests</c> there): a property's region set through <c>SetRegion</c> reaches the reservations of that property anna
///     reads, through their repository. Hers that is deleted, bruno's and the other property's keep none.
/// </summary>
[Collection("document servers")]
public class CascadeHandlerTests(DocumentServers servers)
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

    [Theory]
    [MemberData(nameof(DocumentServers.Stores), MemberType = typeof(DocumentServers))]
    public async Task A_changed_region_reaches_the_reservations_of_its_property(Store store)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var seeded = await DocumentRows.SeededAsync(servers, store);
        var session = DocumentServers.As(seeded, DocumentRows.Anna(_toggle));
        Guid riva, mare;
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = await session.Query<Property>().ToListAsync();
            (riva, mare) = (properties.Single(p => p.Name == "RIVA").Id, properties.Single(p => p.Name == "MARE").Id);
        }

        var dispatcher = new Dispatcher();
        var unitOfWork = new DocUnitOfWork(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope(), dispatcher: dispatcher);
        dispatcher.Handle = e => e is EntityPropertyChanged<Property> changed
            ? new ReservationPropertyRegionCascadeHandler(new Reservation.DocRepository(unitOfWork), unitOfWork).HandleAsync(changed, CancellationToken.None)
            : Task.CompletedTask;

        var property = (await new Property.DocRepository(unitOfWork).GetByIdAsync(riva))!;
        property.SetRegion("north");
        await unitOfWork.SaveChangesAsync();

        List<Reservation> reservations;
        using (_toggle.UseMode(FilterMode.Raw))
            reservations = await session.Query<Reservation>().ToListAsync();
        Assert.Equal("north", reservations.Single(r => r is { OwnerId: "anna", IsDeleted: false } && r.PropertyId == riva).PropertyRegion);
        Assert.Null(reservations.Single(r => r is { OwnerId: "anna", IsDeleted: true }).PropertyRegion); // deleted: not read
        Assert.Null(reservations.Single(r => r is { OwnerId: "bruno" }).PropertyRegion); // not anna's to read
        Assert.Null(reservations.Single(r => r is { OwnerId: "anna" } && r.PropertyId == mare).PropertyRegion); // another property
    }
}

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Persistence.Repository;
using Showcase.Billing;
using Showcase.Billing.Entities;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;
using Xunit;

namespace Showcase.IntegrationTests.CrossCutting;

/// <summary>
///     The generated [CascadeOn] handler has to be registered as an IDomainEventHandler, or cascades
///     silently do nothing, and has to inject the boundary-keyed DbContext, since a non-keyed one is
///     unresolvable in host mode. These prove, against the REAL host DI, that the handler is wired and
///     resolvable — which also exercises its [FromKeyedServices(BillingBoundary)] DbContext dependency
///     (an unresolved keyed dependency would throw during construction).
/// </summary>
public class CascadeWiringTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void CascadeHandler_IsRegistered_AsDomainEventHandlerForRoomTypeChange()
    {
        using var scope = Services.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<IDomainEventHandler<EntityPropertyChanged<RoomType>>>()
            .ToList();

        // The LineItem.UnitPrice cascade handler must be registered, and resolving it constructs the
        // handler, so its boundary-keyed DbContext must resolve too.
        handlers.Should().Contain(h => h.GetType().Name == "LineItemUnitPriceCascadeHandler");
    }

    [Fact]
    public async Task CascadeHandler_OnRoomTypeBaseRateChange_UpdatesLineItemUnitPriceInDatabase()
    {
        // The handler writes the (boxed) new value through the engine. On EF Core a value left as object threw on PostgreSQL
        // ("Expression '@p' ... does not have a type mapping") and the cascade silently never applied; wiring-only coverage cannot see
        // that. This drives the real write and asserts the row actually changed.
        //
        // Seeded and read through the boundary's repositories and unit of work, the surface every engine registers (#873). No operation
        // links a line item to a room type, so the test sets the key with the generated relation setter.
        var roomTypeId = Guid.NewGuid();
        var invoice = Invoice.Create(Guid.NewGuid(), Guid.NewGuid(), 100m, 0m, 100m, DateTimeOffset.UtcNow);
        var lineItem = LineItem.Create(100m, 100m);
        lineItem.SetInvoiceId(invoice.Id);
        lineItem.SetRoomTypeId(roomTypeId);

        using (var scope = Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IRepository<Invoice>>().Add(invoice);
            scope.ServiceProvider.GetRequiredService<IRepository<LineItem>>().Add(lineItem);
            await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BillingBoundary)).SaveChangesAsync();
        }

        using (var scope = Services.CreateScope())
        {
            var handler = scope.ServiceProvider
                .GetServices<IDomainEventHandler<EntityPropertyChanged<RoomType>>>()
                .Single(h => h.GetType().Name == "LineItemUnitPriceCascadeHandler");

            await handler.HandleAsync(new EntityPropertyChanged<RoomType>
            {
                EntityId = roomTypeId,
                PropertyName = "BaseRate",
                OldValue = 100m,
                NewValue = 250m
            }, CancellationToken.None);
        }

        using (var scope = Services.CreateScope())
        {
            var read = await scope.ServiceProvider.GetRequiredService<IRepository<LineItem>>().GetByIdAsync(lineItem.Id);

            read.Should().NotBeNull("the line item was written");
            read!.UnitPrice.Should().Be(250m, "the RoomType.BaseRate change must cascade to the line item");
        }
    }
}

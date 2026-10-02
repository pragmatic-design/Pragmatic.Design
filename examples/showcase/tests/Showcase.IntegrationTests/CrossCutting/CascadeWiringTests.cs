using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
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
        // The handler runs ExecuteUpdate with the (boxed) new value. A value left as object throws on
        // PostgreSQL ("Expression '@p' ... does not have a type mapping") and the cascade silently never
        // applies; wiring-only coverage cannot see that. This drives the real ExecuteUpdate against
        // Postgres and asserts the row actually changed.
        var roomTypeId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var lineItemId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        try
        {
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BillingBoundary));

                // Seed a parent invoice (LineItem.InvoiceId is an enforced FK) and one room-linked line item.
                // AccessScopes is [HasAccessScopes]'s required column: seed a (non-null) empty array so other
                // invoice queries in the shared test database can still materialize the row.
                await db.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO "Invoices"
                       ("PersistenceId","AccessScopes","ReservationId","GuestId","InvoiceNumber","SubTotal","TaxAmount","TotalAmount","Currency","Status","IssuedAt","CreatedAt","IsDeleted")
                     VALUES ({invoiceId},{Array.Empty<string>()},{Guid.NewGuid()},{Guid.NewGuid()},{"INV-CASCADE-W15"},{100m},{0m},{100m},{"EUR"},{0},{now},{now},{false})
                     """);
                await db.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO "LineItems"
                       ("PersistenceId","InvoiceId","RoomTypeId","Description","Quantity","UnitPrice","TotalPrice")
                     VALUES ({lineItemId},{invoiceId},{roomTypeId},{"Room charge"},{1},{100m},{100m})
                     """);
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
                var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BillingBoundary));
                var lineItem = await db.Set<LineItem>().AsNoTracking()
                    .SingleAsync(li => li.PersistenceId == lineItemId);

                lineItem.UnitPrice.Should().Be(250m, "the RoomType.BaseRate change must cascade to the line item");
            }
        }
        finally
        {
            // Leave no footprint in the shared, non-reset test database.
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BillingBoundary));
            await db.Database.ExecuteSqlAsync($"""DELETE FROM "LineItems" WHERE "PersistenceId" = {lineItemId}""");
            await db.Database.ExecuteSqlAsync($"""DELETE FROM "Invoices" WHERE "PersistenceId" = {invoiceId}""");
        }
    }
}

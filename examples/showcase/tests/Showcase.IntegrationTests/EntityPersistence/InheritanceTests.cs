using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Repository;
using Showcase.Billing;
using Showcase.Billing.Dtos;
using Showcase.Billing.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests TPH inheritance mapping for the Fee hierarchy, through the boundary's repositories.
///     Fee is the base type with [Inheritance(Tph, DiscriminatorColumn = "FeeType")].
///     Derived types: ServiceFee, CancellationFee — each stored in the same "Fees" table
///     with a "FeeType" discriminator column.
///     Since there are no HTTP endpoints for fees, tests write and read them through the repositories,
///     with the generated setters the module shows this assembly. The migrated table itself is read in
///     TheFeeHierarchyIsOneTableTests.
/// </summary>
public class InheritanceTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // =========================================================================
    // TPH: ServiceFee persisted with discriminator
    // =========================================================================

    [Fact]
    public async Task Inheritance_ServiceFee_PersistedWithDiscriminator()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var serviceFee = AServiceFee(invoiceId, 25.00m, "Minibar", "Minibar Service");
        await WriteAsync(serviceFee);

        // Read through the base type's repository: the discriminator decides the type that comes back.
        var rawFee = await StoredAsync<Fee>(serviceFee.PersistenceId);

        rawFee.Should().NotBeNull();
        rawFee.Should().BeOfType<ServiceFee>(
            "TPH should return the correct derived type based on FeeType discriminator");

        var typedFee = (ServiceFee)rawFee!;
        typedFee.ServiceName.Should().Be("Minibar Service");
        typedFee.Amount.Should().Be(25.00m);
    }

    // =========================================================================
    // TPH: CancellationFee persisted with discriminator
    // =========================================================================

    [Fact]
    public async Task Inheritance_CancellationFee_PersistedWithDiscriminator()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var cancellationFee = ACancellationFee(invoiceId, 75.00m, "Late cancellation", 0.25m, 300.00m);
        await WriteAsync(cancellationFee);

        // Verify discriminator-based type resolution
        var rawFee = await StoredAsync<Fee>(cancellationFee.PersistenceId);

        rawFee.Should().NotBeNull();
        rawFee.Should().BeOfType<CancellationFee>(
            "TPH should return CancellationFee based on FeeType discriminator");

        var typedFee = (CancellationFee)rawFee!;
        typedFee.PenaltyRate.Should().Be(0.25m);
        typedFee.OriginalAmount.Should().Be(300.00m);
    }

    /// <summary>
    ///     A rate declared <c>[Precision(18, 4)]</c> keeps its four decimals through the store and the migrated column (#731): every
    ///     decimal was <c>numeric(18,2)</c>, and 0.1275 came back 0.13.
    /// </summary>
    [Fact]
    public async Task Inheritance_CancellationFee_KeepsTheFourDecimalsOfItsRate()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var fee = ACancellationFee(invoiceId, 38.25m, "Partial cancellation", 0.1275m, 300.00m);
        await WriteAsync(fee);

        // A fresh scope: the value read is the column's, not the one the writing scope kept.
        var stored = await StoredAsync<CancellationFee>(fee.PersistenceId);

        stored.Should().NotBeNull();
        stored!.PenaltyRate.Should().Be(0.1275m);
    }

    // =========================================================================
    // TPH: the base type's repository returns both derived types
    // =========================================================================

    [Fact]
    public async Task Inheritance_PolymorphicQuery_ReturnsBothTypes()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var serviceFee = AServiceFee(invoiceId, 15.00m, "Room service", "Room Service");
        var cancellationFee = ACancellationFee(invoiceId, 50.00m, "No-show penalty", 0.50m, 100.00m);
        await WriteAsync(serviceFee, cancellationFee);

        // Both read through the base Fee type's repository (a polymorphic read), each by its key.
        var allFees = new[]
        {
            await StoredAsync<Fee>(serviceFee.PersistenceId),
            await StoredAsync<Fee>(cancellationFee.PersistenceId),
        };

        allFees.OfType<ServiceFee>().Should().ContainSingle(
            f => f.ServiceName == "Room Service",
            "ServiceFee should be in the polymorphic result set");

        allFees.OfType<CancellationFee>().Should().ContainSingle(
            f => f.PenaltyRate == 0.50m,
            "CancellationFee should be in the polymorphic result set");
    }

    // =========================================================================
    // TPH: a derived type's repository returns only that type
    // =========================================================================

    [Fact]
    public async Task Inheritance_QueryDerivedType_ReturnsOnlyThatType()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var serviceFee = AServiceFee(invoiceId, 30.00m, "Spa treatment", "Spa");
        var cancellationFee = ACancellationFee(invoiceId, 45.00m, "Early departure penalty", 0.15m, 300.00m);
        await WriteAsync(serviceFee, cancellationFee);

        // The ServiceFee repository filters by discriminator: the CancellationFee row is not one of its own.
        (await StoredAsync<ServiceFee>(serviceFee.PersistenceId)).Should().BeOfType<ServiceFee>();
        (await StoredAsync<ServiceFee>(cancellationFee.PersistenceId)).Should().BeNull(
            "reading ServiceFee should filter by discriminator automatically");

        // And the other way round.
        (await StoredAsync<CancellationFee>(cancellationFee.PersistenceId)).Should().BeOfType<CancellationFee>();
        (await StoredAsync<CancellationFee>(serviceFee.PersistenceId)).Should().BeNull(
            "reading CancellationFee should filter by discriminator automatically");
    }

    // =========================================================================
    // [MapDerived]: the DTO a row maps to is the one its runtime type asks for
    // =========================================================================

    /// <summary>
    ///     A fee row maps to the shape it actually is, not to the base one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mapping every row of a TPH hierarchy to <c>FeeLineDto</c> answers "45 EUR" to a guest
    ///         asking <em>what for</em>. <c>[MapDerived]</c> is the declaration that fixes it: the
    ///         derived DTO must inherit the base (<c>PRAG0330</c>).
    ///     </para>
    ///     <para>
    ///         ⚠️ Read back through the base type's repository and mapped in memory, because the dispatch
    ///         is runtime only — <c>PRAG0331</c> says so on the build. A projection is one expression and
    ///         the discriminator is not known until the row is read.
    ///     </para>
    ///     <para>
    ///         <c>InvoiceNumber</c> is the other half in the same assertion: it flattens through
    ///         <c>Invoice</c>, the navigation the base entity's <c>[Relation]</c> generates. On a derived
    ///         row the attribute is declared on <c>Fee</c>, so the resolver has to read the base's
    ///         attributes too: reading those of <c>ServiceFee</c> alone is <c>PRAG0302</c> at compile time.
    ///         The test sets the navigation to the invoice it reads by key: an engine loads a navigation
    ///         only where a load declares it, and no load of the module declares this one.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task MapDerived_EachRowMapsToItsOwnShape()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        var service = AServiceFee(invoiceId, 45.00m, "Minibar", "Minibar Service");
        var cancellation = ACancellationFee(invoiceId, 80.00m, "Late cancellation", 0.25m, 320.00m);
        var plain = Fee.Create();
        plain.SetInvoiceId(invoiceId);
        plain.SetAmount(10.00m);
        plain.SetCurrency("EUR");
        plain.SetReason("City tax");
        await WriteAsync(service, cancellation, plain);

        var invoice = await StoredAsync<Invoice>(invoiceId);
        invoice.Should().NotBeNull();

        var rows = new List<Fee>();
        foreach (var id in new[] { service.PersistenceId, cancellation.PersistenceId, plain.PersistenceId })
        {
            var row = await StoredAsync<Fee>(id);
            row.Should().NotBeNull();
            row!.Invoice = invoice!;
            rows.Add(row);
        }

        var mapped = rows.Select(FeeLineDto.FromEntity).ToList();

        mapped.Should().ContainSingle(d => d is ServiceFeeLineDto)
            .Which.Should().BeOfType<ServiceFeeLineDto>()
            .Which.ServiceName.Should().Be("Minibar Service",
                "the row is a ServiceFee, so the DTO is the derived shape carrying what it is for");

        mapped.OfType<CancellationFeeLineDto>().Should().ContainSingle()
            .Which.PenaltyRate.Should().Be(0.25m);

        // The control: a plain Fee still maps to the base shape. Without it, "the derived shape is
        // returned" is satisfied by returning a derived shape for everything.
        mapped.Should().ContainSingle(d => d.GetType() == typeof(FeeLineDto))
            .Which.Amount.Should().Be(10.00m);

        // And the member the BASE entity's [Relation] generates reaches every one of them.
        mapped.Should().AllSatisfy(d => d.InvoiceNumber.Should().StartWith("INV-",
            "InvoiceNumber flattens through Fee.Invoice, which is generated on the base entity"));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static ServiceFee AServiceFee(Guid invoiceId, decimal amount, string reason, string serviceName)
    {
        var fee = ServiceFee.Create();
        fee.SetInvoiceId(invoiceId);
        fee.SetAmount(amount);
        fee.SetCurrency("EUR");
        fee.SetReason(reason);
        fee.SetServiceName(serviceName);
        fee.SetServiceDate(DateTimeOffset.UtcNow);
        return fee;
    }

    private static CancellationFee ACancellationFee(Guid invoiceId, decimal amount, string reason, decimal penaltyRate, decimal originalAmount)
    {
        var fee = CancellationFee.Create(penaltyRate, originalAmount);
        fee.SetInvoiceId(invoiceId);
        fee.SetAmount(amount);
        fee.SetCurrency("EUR");
        fee.SetReason(reason);
        return fee;
    }

    /// <summary>Writes the fees through the base type's repository, in one save of the boundary's unit of work.</summary>
    private async Task WriteAsync(params Fee[] fees)
    {
        // The tenant the HTTP client created the invoice under.
        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Fee>>();
        foreach (var fee in fees)
            repository.Add(fee);
        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BillingBoundary)).SaveChangesAsync();
    }

    /// <summary>The row as stored, read through its repository with every filter lifted: this scope is not a request.</summary>
    private async Task<TEntity?> StoredAsync<TEntity>(Guid id) where TEntity : class, Pragmatic.Persistence.Entity.IEntity
    {
        using var tenant = TenantScope.BeginScope("test-tenant");
        using var scope = Services.CreateScope();
        using var all = scope.ServiceProvider.GetRequiredService<IQueryFilterToggle>().DisableAll();
        return await scope.ServiceProvider.GetRequiredService<IReadRepository<TEntity>>().GetByIdAsync(id);
    }

    /// <summary>
    ///     Creates an invoice by confirming a reservation (triggers domain event → invoice creation).
    ///     Returns the invoice ID.
    /// </summary>
    private async Task<Guid> CreateInvoiceViaReservationAsync()
    {
        var guest = await PostAsync<JsonElement>("/api/guests", new
        {
            firstName = "TPH",
            lastName = "Test",
            email = $"tph.{Guid.NewGuid():N}@test.com"
        });
        var guestId = guest.GetProperty("id").GetGuid();

        var prop = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"TH-{Guid.NewGuid():N}"[..12],
            name = $"TphProp-{Guid.NewGuid():N}"[..20],
            city = "Milan",
            country = "IT",
            starRating = 4
        });
        var propertyId = prop.GetProperty("id").GetGuid();

        var rt = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "TPH Room",
            code = "TPH",
            baseRate = 150m,
            totalRooms = 5
        });
        var roomTypeId = rt.GetProperty("id").GetGuid();

        var resResponse = await PostAsync("/api/reservations?api-version=1.0", new
        {
            request = new
            {
                guestId,
                propertyId,
                roomTypeId,
                checkIn = DateTimeOffset.UtcNow.AddDays(14).ToString("O"),
                checkOut = DateTimeOffset.UtcNow.AddDays(17).ToString("O"),
                numberOfGuests = 2
            }
        });
        resResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservationId = await resResponse.Content.ReadFromJsonAsync<Guid>(JsonOptions);

        // Confirm reservation → triggers ReservationConfirmed → creates invoice
        var confirmResponse = await PostAsync($"/api/reservations/{reservationId}/confirm", new { });
        confirmResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Get the created invoice
        var invoiceSearch = await GetAsync<JsonElement>(
            $"/api/invoices/search?reservationId={reservationId}");
        var items = invoiceSearch.GetProperty("items");
        items.GetArrayLength().Should().BeGreaterOrEqualTo(1,
            "Confirming a reservation should auto-create an invoice");

        return items[0].GetProperty("id").GetGuid();
    }
}

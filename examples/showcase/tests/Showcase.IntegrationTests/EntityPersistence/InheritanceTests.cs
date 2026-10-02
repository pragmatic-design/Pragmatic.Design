using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Pragmatic.Migrations.Introspection;
using Showcase.Billing.Dtos;
using Showcase.Billing.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     Tests TPH inheritance mapping for Fee hierarchy via the BillingDbContext.
///     Fee is the base type with [Inheritance(Tph, DiscriminatorColumn = "FeeType")].
///     Derived types: ServiceFee, CancellationFee — each stored in the same "Fees" table
///     with a "FeeType" discriminator column.
///     Since there are no HTTP endpoints for fees, tests interact with the DB directly.
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

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<BillingDbContext>();

        // Create a ServiceFee directly via EF Core
        var serviceFee = new ServiceFee();
        // Use EF Core entry to set private-setter properties
        db.Set<Fee>().Add(serviceFee);
        var entry = db.Entry(serviceFee);
        entry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        entry.Property(nameof(Fee.Amount)).CurrentValue = 25.00m;
        entry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        entry.Property(nameof(Fee.Reason)).CurrentValue = "Minibar";
        entry.Property(nameof(ServiceFee.ServiceName)).CurrentValue = "Minibar Service";
        entry.Property(nameof(ServiceFee.ServiceDate)).CurrentValue = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync();

        // Verify the discriminator is set correctly by querying raw
        // Note: use PersistenceId (EF-mapped) not Id (computed property, not translatable)
        var feeId = serviceFee.PersistenceId;
        var rawFee = await db.Set<Fee>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.PersistenceId == feeId);

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

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<BillingDbContext>();

        // Create a CancellationFee directly via EF Core
        var cancellationFee = new CancellationFee();
        db.Set<Fee>().Add(cancellationFee);
        var entry = db.Entry(cancellationFee);
        entry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        entry.Property(nameof(Fee.Amount)).CurrentValue = 75.00m;
        entry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        entry.Property(nameof(Fee.Reason)).CurrentValue = "Late cancellation";
        entry.Property(nameof(CancellationFee.PenaltyRate)).CurrentValue = 0.25m;
        entry.Property(nameof(CancellationFee.OriginalAmount)).CurrentValue = 300.00m;

        await db.SaveChangesAsync();

        // Verify discriminator-based type resolution
        // Note: use PersistenceId (EF-mapped) not Id (computed property, not translatable)
        var feeId = cancellationFee.PersistenceId;
        var rawFee = await db.Set<Fee>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.PersistenceId == feeId);

        rawFee.Should().NotBeNull();
        rawFee.Should().BeOfType<CancellationFee>(
            "TPH should return CancellationFee based on FeeType discriminator");

        var typedFee = (CancellationFee)rawFee!;
        typedFee.PenaltyRate.Should().Be(0.25m);
        typedFee.OriginalAmount.Should().Be(300.00m);
    }

    // =========================================================================
    // TPH: Polymorphic query returns both derived types
    // =========================================================================

    [Fact]
    public async Task Inheritance_PolymorphicQuery_ReturnsBothTypes()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<BillingDbContext>();

        // Create a ServiceFee
        var serviceFee = new ServiceFee();
        db.Set<Fee>().Add(serviceFee);
        var sfEntry = db.Entry(serviceFee);
        sfEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        sfEntry.Property(nameof(Fee.Amount)).CurrentValue = 15.00m;
        sfEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        sfEntry.Property(nameof(Fee.Reason)).CurrentValue = "Room service";
        sfEntry.Property(nameof(ServiceFee.ServiceName)).CurrentValue = "Room Service";
        sfEntry.Property(nameof(ServiceFee.ServiceDate)).CurrentValue = DateTimeOffset.UtcNow;

        // Create a CancellationFee
        var cancellationFee = new CancellationFee();
        db.Set<Fee>().Add(cancellationFee);
        var cfEntry = db.Entry(cancellationFee);
        cfEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        cfEntry.Property(nameof(Fee.Amount)).CurrentValue = 50.00m;
        cfEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        cfEntry.Property(nameof(Fee.Reason)).CurrentValue = "No-show penalty";
        cfEntry.Property(nameof(CancellationFee.PenaltyRate)).CurrentValue = 0.50m;
        cfEntry.Property(nameof(CancellationFee.OriginalAmount)).CurrentValue = 100.00m;

        await db.SaveChangesAsync();

        // Query all fees for this invoice using the base Fee type (polymorphic query)
        var allFees = await db.Set<Fee>()
            .IgnoreQueryFilters()
            .Where(f => f.InvoiceId == invoiceId)
            .ToListAsync();

        allFees.Should().HaveCountGreaterOrEqualTo(2,
            "Polymorphic query on base Fee should return both ServiceFee and CancellationFee");

        allFees.OfType<ServiceFee>().Should().ContainSingle(
            f => f.ServiceName == "Room Service",
            "ServiceFee should be in the polymorphic result set");

        allFees.OfType<CancellationFee>().Should().ContainSingle(
            f => f.PenaltyRate == 0.50m,
            "CancellationFee should be in the polymorphic result set");
    }

    // =========================================================================
    // TPH: Querying derived type directly returns only that type
    // =========================================================================

    [Fact]
    public async Task Inheritance_QueryDerivedType_ReturnsOnlyThatType()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider
            .GetRequiredService<BillingDbContext>();

        // Create both fee types
        var serviceFee = new ServiceFee();
        db.Set<Fee>().Add(serviceFee);
        var sfEntry = db.Entry(serviceFee);
        sfEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        sfEntry.Property(nameof(Fee.Amount)).CurrentValue = 30.00m;
        sfEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        sfEntry.Property(nameof(Fee.Reason)).CurrentValue = "Spa treatment";
        sfEntry.Property(nameof(ServiceFee.ServiceName)).CurrentValue = "Spa";
        sfEntry.Property(nameof(ServiceFee.ServiceDate)).CurrentValue = DateTimeOffset.UtcNow;

        var cancellationFee = new CancellationFee();
        db.Set<Fee>().Add(cancellationFee);
        var cfEntry = db.Entry(cancellationFee);
        cfEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        cfEntry.Property(nameof(Fee.Amount)).CurrentValue = 45.00m;
        cfEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        cfEntry.Property(nameof(Fee.Reason)).CurrentValue = "Early departure penalty";
        cfEntry.Property(nameof(CancellationFee.PenaltyRate)).CurrentValue = 0.15m;
        cfEntry.Property(nameof(CancellationFee.OriginalAmount)).CurrentValue = 300.00m;

        await db.SaveChangesAsync();

        // Query only ServiceFee — EF Core adds WHERE FeeType = 'ServiceFee' automatically
        var serviceFees = await db.Set<ServiceFee>()
            .IgnoreQueryFilters()
            .Where(f => f.InvoiceId == invoiceId)
            .ToListAsync();

        serviceFees.Should().OnlyContain(f => f is ServiceFee,
            "Querying DbSet<ServiceFee> should filter by discriminator automatically");

        // Query only CancellationFee
        var cancellationFees = await db.Set<CancellationFee>()
            .IgnoreQueryFilters()
            .Where(f => f.InvoiceId == invoiceId)
            .ToListAsync();

        cancellationFees.Should().OnlyContain(f => f is CancellationFee,
            "Querying DbSet<CancellationFee> should filter by discriminator automatically");
    }

    // =========================================================================
    // The derived types declare [Entity] themselves
    // =========================================================================

    /// <summary>
    ///     The hierarchy is one table in the migrated schema, and it holds the derived columns.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>ServiceFee</c> and <c>CancellationFee</c> now carry <c>[Entity]</c> — the form
    ///         <c>[Inheritance]</c>'s own documentation shows, and the only form that gives a derived
    ///         type a repository, mutations and permissions of its own. Until today it compiled and
    ///         killed the boundary at first use: the generated configuration claimed <c>ToTable</c> and
    ///         <c>HasKey</c> for a derived type, which EF refuses while building the model, and the
    ///         migration schema counted the derived types as tables on top of merging their columns into
    ///         the root's — three tables where TPH wants one.
    ///     </para>
    ///     <para>
    ///         Read from the live database through the introspector, so it says what the migration
    ///         created rather than what the generator meant. Every other case in this class is the other
    ///         half of the proof: they build the EF model and write rows through it.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task TheHierarchy_IsOneTableInTheMigratedSchema()
    {
        await using var connection = new NpgsqlConnection(Fixture.FinancialConnectionString);
        await connection.OpenAsync();

        var schema = await new PostgreSqlSchemaIntrospector().IntrospectAsync(connection);
        var tables = schema.Tables.Select(t => t.Name).ToList();

        tables.Should().Contain("Fees", "the root's table is the hierarchy's table");
        tables.Should().NotContain("ServiceFees", "TPH is one table");
        tables.Should().NotContain("CancellationFeses", "and one table for every derived type");

        var fees = schema.Tables.First(t => t.Name == "Fees");
        var columns = fees.Columns.Select(c => c.Name).ToList();
        columns.Should().Contain("FeeType", "the discriminator [Inheritance] named");
        columns.Should().Contain("ServiceName", "ServiceFee's own column, merged into the root's table");
        columns.Should().Contain("PenaltyRate", "and CancellationFee's");
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
    ///         ⚠️ Read back through the context and mapped in memory, because the dispatch is runtime
    ///         only — <c>PRAG0331</c> says so on the build. A projection is one expression and the
    ///         discriminator is not known until the row is read.
    ///     </para>
    ///     <para>
    ///         <c>InvoiceNumber</c> is the other half in the same assertion: it flattens through
    ///         <c>Invoice</c>, the navigation the base entity's <c>[Relation]</c> generates. On a derived
    ///         row the attribute is declared on <c>Fee</c>, so the resolver has to read the base's
    ///         attributes too: reading those of <c>ServiceFee</c> alone is <c>PRAG0302</c> at compile time.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task MapDerived_EachRowMapsToItsOwnShape()
    {
        var invoiceId = await CreateInvoiceViaReservationAsync();

        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();

        var service = new ServiceFee();
        db.Set<Fee>().Add(service);
        var serviceEntry = db.Entry(service);
        serviceEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        serviceEntry.Property(nameof(Fee.Amount)).CurrentValue = 45.00m;
        serviceEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        serviceEntry.Property(nameof(Fee.Reason)).CurrentValue = "Minibar";
        serviceEntry.Property(nameof(ServiceFee.ServiceName)).CurrentValue = "Minibar Service";
        serviceEntry.Property(nameof(ServiceFee.ServiceDate)).CurrentValue = DateTimeOffset.UtcNow;

        var cancellation = new CancellationFee();
        db.Set<Fee>().Add(cancellation);
        var cancellationEntry = db.Entry(cancellation);
        cancellationEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        cancellationEntry.Property(nameof(Fee.Amount)).CurrentValue = 80.00m;
        cancellationEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        cancellationEntry.Property(nameof(Fee.Reason)).CurrentValue = "Late cancellation";
        cancellationEntry.Property(nameof(CancellationFee.PenaltyRate)).CurrentValue = 0.25m;
        cancellationEntry.Property(nameof(CancellationFee.OriginalAmount)).CurrentValue = 320.00m;

        var plain = new Fee();
        db.Set<Fee>().Add(plain);
        var plainEntry = db.Entry(plain);
        plainEntry.Property(nameof(Fee.InvoiceId)).CurrentValue = invoiceId;
        plainEntry.Property(nameof(Fee.Amount)).CurrentValue = 10.00m;
        plainEntry.Property(nameof(Fee.Currency)).CurrentValue = "EUR";
        plainEntry.Property(nameof(Fee.Reason)).CurrentValue = "City tax";

        await db.SaveChangesAsync();

        var rows = await db.Set<Fee>()
            .IgnoreQueryFilters()
            .Include(f => f.Invoice)
            .Where(f => f.InvoiceId == invoiceId)
            .ToListAsync();

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

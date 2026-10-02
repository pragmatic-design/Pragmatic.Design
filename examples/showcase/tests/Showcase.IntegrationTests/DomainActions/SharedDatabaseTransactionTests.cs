using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Catalog;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     What two boundaries mapped to the same physical database actually share today.
/// </summary>
/// <remarks>
///     <para>
///         The host knows which boundaries share a database — <c>[Include&lt;BookingModule,
///         ShowcaseAppDatabase&gt;]</c> — and already uses that knowledge to validate
///         <c>[ReadAccess&lt;T&gt;]</c>. It does not use it for writes: each boundary commits through
///         its own <c>DbContext</c>, so a chain crossing from Booking to Catalog is two transactions
///         even though one would do.
///     </para>
///     <para>
///         These tests record the starting state rather than a wish. Whether the connection can be
///         shared is the question that decides whether a single transaction is reachable at all, and
///         the answer belongs in a test rather than in an assumption.
///     </para>
/// </remarks>
public class SharedDatabaseTransactionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void BoundariesOnTheSameDatabase_HaveSeparateDbContexts()
    {
        using var scope = Services.CreateScope();

        var booking = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));
        var catalog = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        ReferenceEquals(booking, catalog).Should().BeFalse(
            "one DbContext per boundary is the design — the isolation that makes a boundary a boundary");
    }

    /// <summary>
    ///     The measurement that decides whether one transaction across them is reachable.
    /// </summary>
    /// <remarks>
    ///     Separate connections mean separate transactions, full stop: <c>UseTransaction</c> requires
    ///     both contexts to be on the same <c>DbConnection</c>. Sharing one is therefore the price of
    ///     atomicity here, and this test says which side of that line the framework starts on.
    /// </remarks>
    [Fact]
    public void BoundariesOnTheSameDatabase_DoNotShareAConnection()
    {
        using var scope = Services.CreateScope();

        var booking = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));
        var catalog = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(CatalogBoundary));

        var bookingConnection = booking.Database.GetDbConnection();
        var catalogConnection = catalog.Database.GetDbConnection();

        bookingConnection.ConnectionString.Should().Be(catalogConnection.ConnectionString,
            "both boundaries are mapped to ShowcaseAppDatabase, so they address the same server");

        ReferenceEquals(bookingConnection, catalogConnection).Should().BeFalse(
            "same database, different connections — which is why a chain across them is two "
            + "transactions today, and why one transaction needs the connection to be shared first");
    }

    /// <summary>
    ///     A boundary on a different database shares nothing, and no amount of wiring will change it.
    /// </summary>
    [Fact]
    public void BoundariesOnDifferentDatabases_DoNotEvenShareAServer()
    {
        using var scope = Services.CreateScope();

        var booking = scope.ServiceProvider.GetRequiredKeyedService<DbContext>(typeof(BookingBoundary));
        var billing = scope.ServiceProvider
            .GetRequiredKeyedService<DbContext>(typeof(Showcase.Billing.BillingBoundary));

        booking.Database.GetDbConnection().ConnectionString
            .Should().NotBe(billing.Database.GetDbConnection().ConnectionString,
                "Billing is mapped to ShowcaseFinancialDatabase. This is the case PRAG0424 is for: "
                + "no shared transaction is possible, so the answer is [UndoWith<T>] or a saga");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Catalog;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     Whether two boundaries' <c>DbContext</c>s share a connection: the measurement that decides whether
///     one EF Core transaction across them is reachable.
/// </summary>
/// <remarks>
///     <c>UseTransaction</c> requires both contexts to be on the same <c>DbConnection</c>, so this reads
///     EF Core's own connection object: a mechanism of the provider, which the engines do not have. That
///     each boundary has a unit of work of its own, on every engine, is
///     <c>SharedDatabaseTransactionTests</c>.
/// </remarks>
public class BoundaryDbConnectionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    /// <summary>
    ///     Separate connections mean separate transactions, full stop. Sharing one is therefore the price of
    ///     atomicity here, and this test says which side of that line the framework starts on.
    /// </summary>
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

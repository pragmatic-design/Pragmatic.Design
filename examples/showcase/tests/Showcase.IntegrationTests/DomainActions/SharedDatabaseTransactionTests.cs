using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
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
///         its own unit of work, so a chain crossing from Booking to Catalog is two transactions
///         even though one would do.
///     </para>
///     <para>
///         These tests record the starting state rather than a wish. Whether the connection can be
///         shared is the question that decides whether a single transaction is reachable at all; on
///         EF Core it is answered by <c>BoundaryDbConnectionTests</c>, which reads the
///         <c>DbContext</c>'s connection.
///     </para>
/// </remarks>
public class SharedDatabaseTransactionTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void BoundariesOnTheSameDatabase_HaveSeparateUnitsOfWork()
    {
        using var scope = Services.CreateScope();

        var booking = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(BookingBoundary));
        var catalog = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(CatalogBoundary));

        ReferenceEquals(booking, catalog).Should().BeFalse(
            "one unit of work per boundary is the design — the isolation that makes a boundary a boundary");
    }
}

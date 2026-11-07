using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[LoadEntity(Include)]</c> of a required reference on the five databases (#496): the reservation read with its property through an INNER
///     JOIN, into a navigation that is not nullable, as the Showcase's <c>RoomType.Property</c> is. A property of a scope the caller does not hold
///     is read with the reservation: the owner and scopes filter the root, not a reference, as on EF Core (#580).
/// </summary>
[Collection("pragmatic databases")]
public class RequiredIncludeTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_required_reference_is_read_with_its_entity_whatever_its_owner_and_scopes(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        using var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        Guid atRiva, atMare;
        using (_toggle.UseMode(FilterMode.Raw))
        {
            var properties = (await session.Query<Property>().ToListAsync()).ToDictionary(p => p.Id, p => p.Name);
            var annas = (await session.Query<Reservation>().ToListAsync()).Where(r => r.OwnerId == "anna").ToList();
            atRiva = annas.Single(r => properties[r.PropertyId] == "RIVA").Id;
            atMare = annas.Single(r => properties[r.PropertyId] == "MARE").Id; // MARE is desk-south's: anna does not see it
        }

        var invoker = new ReadReservationPropertyAction.Invoker(new Reservation.SqlReadRepository(session), new ServiceCollection().BuildServiceProvider());

        Assert.Equal("RIVA", (await invoker.InvokeAsync(new ReadReservationPropertyAction { Id = atRiva })).Value);
        Assert.Equal("MARE", (await invoker.InvokeAsync(new ReadReservationPropertyAction { Id = atMare })).Value);
    }
}

using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Pragmatic.Result.Http;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[LoadEntity(Include)]</c> of a required reference on the five databases (#496): the reservation read with its property through an INNER
///     JOIN, into a navigation that is not nullable, as the Showcase's <c>RoomType.Property</c> is. A property the caller cannot see takes the row
///     with it, as EF Core's INNER JOIN over a filtered target does.
/// </summary>
[Collection("pragmatic databases")]
public class RequiredIncludeTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_required_reference_is_read_with_its_entity_and_a_hidden_one_hides_the_row(SqlDialect dialect)
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
        Assert.IsType<NotFoundError>((await invoker.InvokeAsync(new ReadReservationPropertyAction { Id = atMare })).Error);
    }
}

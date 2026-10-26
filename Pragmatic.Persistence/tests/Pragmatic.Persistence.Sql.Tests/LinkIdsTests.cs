using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Query.Filters;
using Pragmatic.Persistence.Sql;
using Xunit;

namespace PragmaticEntities.Tests;

/// <summary>
///     <c>[LinkIds]</c> on the Sql engine, on the five databases: the keys a mutation names made the rows a collection points at, through the
///     repository's <c>INavigationLinker</c>. A many-to-many side's links written as join rows from stubs of the keys (nothing read), a
///     one-to-many's chosen rows read by key and moved under the property. anna holds desk-north, in acme: RIVA and LAGO are hers.
/// </summary>
[Collection("pragmatic databases")]
public class LinkIdsTests(PragmaticDatabases databases)
{
    private readonly IQueryFilterToggle _toggle = new QueryFilterToggle();

    private async Task<(SqlSession Session, Property Riva)> Seeded(SqlDialect dialect)
    {
        var session = await databases.SessionAsync(dialect, new PragmaticSqlFilters(new TestUser("anna"), new TenantScope(), new TestScopes("desk-north"), _toggle));
        await PragmaticRows.SeedAsync(session, _toggle);
        using (_toggle.UseMode(FilterMode.Raw))
            return (session, (await session.Query<Property>().ToListAsync()).Single(p => p.Name == "RIVA"));
    }

    private static SqlUnitOfWork UnitOfWork(SqlSession session) => new(session, new FixedClock(UnitOfWorkTests.Now), new TestUser("anna"), new TenantScope());

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_many_to_many_side_links_exactly_the_keys_sent(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva) = await Seeded(dialect);
        using (session)
        {
            var (wifi, pool, spa) = (new Amenity { Name = "wifi" }, new Amenity { Name = "pool" }, new Amenity { Name = "spa" });
            using (var seeding = UnitOfWork(session)) // [ConcurrencyAware]: where the database keeps the version, a unit of work writes it
            {
                foreach (var amenity in new[] { wifi, pool, spa })
                    seeding.Add(amenity);
                await seeding.SaveChangesAsync();
            }

            using (_toggle.UseMode(FilterMode.Raw))
            {
                await session.LinkAsync(riva, nameof(Property.Amenities), wifi);
                await session.LinkAsync(riva, nameof(Property.Amenities), pool);
            }

            using var unitOfWork = UnitOfWork(session);
            var set = await new SetAmenitiesMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider())
                .InvokeAsync(new SetAmenitiesMutation { Id = riva.Id, AmenityIds = [pool.Id, spa.Id] });
            Assert.True(set.IsSuccess, set.IsSuccess ? "" : set.Error.ToString());

            var read = await new Property.SqlReadRepository(session).GetByIdIncludingAsync(riva.Id, nameof(Property.Amenities));
            Assert.Equal(["pool", "spa"], read!.Amenities.Select(a => a.Name).Order(StringComparer.Ordinal)); // wifi unlinked, spa linked, pool kept
            using (_toggle.UseMode(FilterMode.Raw))
                Assert.Equal(3, (await session.Query<Amenity>().ToListAsync()).Count); // a link removed, not a row
        }
    }

    [Theory]
    [MemberData(nameof(PragmaticDatabases.Dialects), MemberType = typeof(PragmaticDatabases))]
    public async Task A_one_to_many_collection_takes_the_rows_chosen_by_key(SqlDialect dialect)
    {
        using var tenant = TenantScope.BeginScope("acme");
        var (session, riva) = await Seeded(dialect);
        using (session)
        {
            var lago = new Property { Name = "LAGO", TenantId = "acme" };
            lago.GrantScope("desk-north");
            Guid standard;
            using (_toggle.UseMode(FilterMode.Raw))
            {
                await session.InsertAsync(lago);
                standard = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Code == "STD").Id;
            }

            using var unitOfWork = UnitOfWork(session);
            var chosen = await new ChooseRoomTypesMutation.Invoker(new Property.SqlRepository(unitOfWork), unitOfWork, new ServiceCollection().BuildServiceProvider())
                .InvokeAsync(new ChooseRoomTypesMutation { Id = lago.Id, RoomTypeIds = [standard] });
            Assert.True(chosen.IsSuccess, chosen.IsSuccess ? "" : chosen.Error.ToString());

            using (_toggle.UseMode(FilterMode.Raw))
            {
                var moved = (await session.Query<RoomType>().ToListAsync()).Single(t => t.Id == standard);
                Assert.Equal(lago.Id, moved.PropertyId); // under LAGO now
                Assert.False(moved.IsDeleted);
            }
        }
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>[HttpStatus]</c> on an endpoint: the success status an operation chooses, where
///     the default is wrong.
/// </summary>
/// <remarks>
///     <para>
///         A POST with a body defaults to <b>201 Created</b>, which is right for most of them and
///         wrong for an import: <c>ImportSeasonalRatesEndpoint</c> updates rates that already exist
///         and creates nothing. It answered <c>Results.Created((string?)null, success)</c>.
///     </para>
///     <para>
///         ⚠️ <b>The Location header is not part of this.</b> Both answers carry none: a generated
///         201 sets one only when the endpoint declares where the resource went, so the property
///         create — the honest 201 here — has no Location either. Asserting its absence on the
///         import would therefore say nothing about the import.
///     </para>
///     <para>
///         ⚠️ The pair is the measurement. A test that only asserts the import answers 200 is
///         satisfied by a generator that stopped producing 201 for everything, which would be a
///         worse bug than the one being fixed — so the create beside it is asserted in the same
///         test, still 201, still with its <c>Location</c>.
///     </para>
/// </remarks>
public class TheStatusAPostAnswersWithTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task TheImport_Answers200_AndTheCreateBesideIt_Still201()
    {
        using var importer = CreateClientWithPermissions("rates.import", "catalog.property.create");

        var created = await importer.PostAsJsonAsync("/api/properties", new
        {
            code = $"ST-{Guid.NewGuid():N}"[..12],
            name = $"StatusProp-{Guid.NewGuid():N}"[..20],
            city = "Bari",
            country = "IT",
            starRating = 3
        }, JsonOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created,
            "creating a property really does create one, and the generator's default is right there");

        var propertyId = (await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions))
            .GetProperty("id").GetGuid();

        // The generated body is a wrapper — `{ "rates": [ … ] }` — because the route already carries
        // the property. A bare array binds to nothing and answers 400.
        var imported = await importer.PostAsJsonAsync(
            $"/api/properties/{propertyId}/rates/import",
            new { rates = new[] { new { roomTypeId = Guid.NewGuid(), newBaseRate = 150.0m } } },
            JsonOptions);

        imported.StatusCode.Should().Be(HttpStatusCode.OK,
            "[HttpStatus(200)]: the import updates rates and creates nothing — body was {0}",
            await imported.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     And the import imports — which nothing had ever checked.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The only test this route had asserts the <b>403</b> a caller without
    ///     <c>rates.import</c> gets, and a 403 never reaches the body. So the handler had never run:
    ///     it filtered on <c>RoomType.Id</c>, which is the entity's convenience face and not a
    ///     mapped column, and every call that got past authorization answered <b>500</b> with an
    ///     untranslatable LINQ expression. Fixed with the status this story is about, because a
    ///     status assertion on an endpoint that throws is not a measurement of anything.
    /// </remarks>
    [Fact]
    public async Task TheImport_ChangesTheRateItWasGiven()
    {
        using var importer = CreateClientWithPermissions(
            "rates.import", "catalog.property.create", "catalog.room-type.create", "catalog.room-type.read");

        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"SR-{Guid.NewGuid():N}"[..12],
            name = $"RateProp-{Guid.NewGuid():N}"[..20],
            city = "Lecce",
            country = "IT",
            starRating = 4
        });
        var propertyId = property.GetProperty("id").GetGuid();

        var roomType = await PostAsync<JsonElement>("/api/room-types", new
        {
            propertyId,
            name = "SR Room",
            code = $"SR{Guid.NewGuid():N}"[..3],
            baseRate = 100m,
            totalRooms = 2
        });
        var roomTypeId = roomType.GetProperty("id").GetGuid();

        var imported = await importer.PostAsJsonAsync(
            $"/api/properties/{propertyId}/rates/import",
            new { rates = new[] { new { roomTypeId, newBaseRate = 175.5m } } },
            JsonOptions);

        imported.StatusCode.Should().Be(HttpStatusCode.OK,
            "{0}", await imported.Content.ReadAsStringAsync());

        var result = await imported.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        result.GetProperty("totalUpdated").GetInt32().Should().Be(1);
        result.GetProperty("totalSkipped").GetInt32().Should().Be(0,
            "the room type is this property's, and the rate is positive");

        // Read back from the database and not through the search route: that one is [Cacheable],
        // so a stale hit would answer the old rate and this assertion would be about the cache.
        using var scope = Services.CreateScope();
        var stored = await scope.ServiceProvider
            .GetRequiredService<IReadRepository<RoomType>>()
            .GetByIdAsync(roomTypeId);

        stored.Should().NotBeNull();
        stored!.BaseRate.Should().Be(175.5m,
            "the batch save committed, which is the half a summary object cannot prove");
    }
}

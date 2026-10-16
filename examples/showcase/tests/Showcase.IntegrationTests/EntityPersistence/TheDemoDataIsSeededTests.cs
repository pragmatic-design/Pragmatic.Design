using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.Host.Seeding;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.EntityPersistence;

/// <summary>
///     <c>Showcase:SeedDemoData</c> puts the demo hotels into the tenant <c>demo</c> when a Development host
///     starts, once, and nothing when it is off (#229).
/// </summary>
/// <remarks>
///     <para>
///         Three hosts in one test, in order, because the order is the point: the database starts with no demo
///         hotels, a host with the option off adds none, one with it on adds the three, and a second one with
///         it on adds nothing more. Split into separate tests, the "off" reading would depend on whether the
///         "on" one had run first in the shared database.
///     </para>
///     <para>
///         Read through <c>GET /api/properties/search</c> as the demo tenant, which is what a reader following
///         the README does after <c>dotnet run</c>.
///     </para>
/// </remarks>
[Collection(IntegrationTestCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TheDemoDataIsSeededTests(PostgresFixture fixture)
{
    private static readonly string[] DemoCodes = ["DEMO-MILAN", "DEMO-PARIS", "DEMO-ROME"];

    private static readonly Dictionary<string, string?> SeedOn = new() { ["Showcase:SeedDemoData"] = "true" };

    [Fact]
    public async Task OffSeedsNothing_OnSeedsTheHotelsOnce()
    {
        await using (var off = new ShowcaseWebFactory(fixture))
        {
            (await DemoHotelCodesAsync(off)).Should().BeEmpty("the option is off, so the host seeds nothing");
        }

        await using (var on = new ShowcaseWebFactory(fixture, SeedOn))
        {
            (await DemoHotelCodesAsync(on)).Should().Equal(DemoCodes);
        }

        await using (var again = new ShowcaseWebFactory(fixture, SeedOn))
        {
            (await DemoHotelCodesAsync(again)).Should().Equal(
                DemoCodes, "a restart finds the hotels by code and adds no second copy");
        }
    }

    private static async Task<List<string>> DemoHotelCodesAsync(ShowcaseWebFactory host)
    {
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", ShowcaseDemoDataSeeder.DemoTenant);
        client.DefaultRequestHeaders.Add("X-User-Id", "demo-reader");
        client.DefaultRequestHeaders.Add("X-User-Permissions", "catalog.*");

        var page = await client.GetFromJsonAsync<JsonElement>("/api/properties/search?pageSize=50");
        return [.. page.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)
            .Where(code => code.StartsWith("DEMO-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
    }
}

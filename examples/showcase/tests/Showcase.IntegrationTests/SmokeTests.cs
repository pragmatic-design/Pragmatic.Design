using System.Net;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests;

/// <summary>
///     Smoke tests — verify the app boots, databases migrate, and endpoints respond.
/// </summary>
public class SmokeTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task App_Boots_And_Endpoints_Respond()
    {
        // Verifies the full Pragmatic pipeline boots: SG-generated DI, middleware, endpoint mapping
        var response = await GetRawAsync("/api/amenities/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Properties_Search_Returns_Ok_With_EmptyResult()
    {
        var response = await GetRawAsync("/api/properties/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Guests_Search_Returns_Ok_With_EmptyResult()
    {
        var response = await GetRawAsync("/api/guests/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Invoices_Search_Returns_Ok_With_EmptyResult()
    {
        var response = await GetRawAsync("/api/invoices/search");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

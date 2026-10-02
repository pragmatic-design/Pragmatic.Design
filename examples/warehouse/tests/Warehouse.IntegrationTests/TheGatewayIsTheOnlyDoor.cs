using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     The gateway is the only door: it turns away a request with no valid token before any
///     service sees it, routes each prefix to its service, and spreads Stock's reads over both instances.
/// </summary>
/// <remarks>
///     <para>
///         Who answered is read from <c>X-Served-By</c>, which every host writes on every response
///         (<c>TheInstanceThatAnswered</c>) and the gateway never does: a refusal without it is the
///         gateway's, one with it is a service's.
///     </para>
///     <para>
///         The edge decides <b>who</b> — a token signed with the key the services share — and each
///         service still decides <b>what</b>: a valid token whose role holds no permission there is
///         refused by the service.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class TheGatewayIsTheOnlyDoor(WarehouseFixture warehouse)
{
    private const string ServedBy = "X-Served-By";

    [Theory]
    [InlineData("/orders/api/orders/00000000-0000-0000-0000-000000000001")]
    [InlineData("/warehouse/api/levels")]
    [InlineData("/shipping/api/shipments")]
    public async Task ARequestWithoutAToken_Is401_FromTheGateway(string path)
    {
        using var anonymous = warehouse.Gateway.CreateClient();

        using var refused = await anonymous.GetAsync(path);

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.Contains(ServedBy).Should().BeFalse(
            "the gateway refused it: no service answered, so no service saw it");
    }

    [Fact]
    public async Task ATokenNotSignedByTheKey_Is401_FromTheGateway()
    {
        using var forged = warehouse.Gateway.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiJub2JvZHkiLCJyb2xlIjoic3RvY2stbWFuYWdlciJ9.c2lnbmVkLWJ5LW5vYm9keQ");

        using var refused = await forged.GetAsync("/warehouse/api/levels");

        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        refused.Headers.Contains(ServedBy).Should().BeFalse();
    }

    [Fact]
    public async Task AValidTokenWithoutThePermission_Is403_FromTheService()
    {
        // A real token — signed with the shared key — for a role Stock grants nothing to.
        var token = warehouse.StockA.Services.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: $"shipping-{Guid.NewGuid():N}", roles: [ShippingCalls.Clerk]).Token;
        using var client = warehouse.Gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var refused = await client.GetAsync("/warehouse/api/levels");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        refused.Headers.Contains(ServedBy).Should().BeTrue(
            "the gateway let a valid token through, and the service decided what it may do");
    }

    [Fact]
    public async Task TwentyAvailabilityReads_AreServedByBothStockInstances()
    {
        var product = await StockCalls.InStockAsync(warehouse.StockA, 3);
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);

        var servedBy = new HashSet<string>(StringComparer.Ordinal);
        for (var read = 0; read < 20; read++)
        {
            using var response = await manager.GetAsync($"api/levels?productId={product.Id}");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            servedBy.Add(response.Headers.GetValues(ServedBy).Single());
        }

        servedBy.Should().BeEquivalentTo([warehouse.StockA.ServedBy, warehouse.StockB.ServedBy],
            "the Stock route names both instances, and the gateway balances between them");
    }

    /// <summary>The control: a path under none of the three prefixes is the gateway's 404.</summary>
    [Fact]
    public async Task APathOutsideTheThreePrefixes_Is404_FromTheGateway()
    {
        using var client = warehouse.Gateway.CreateClient();

        using var missing = await client.GetAsync("/billing/api/invoices");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missing.Headers.Contains(ServedBy).Should().BeFalse("no route matched, so nothing was forwarded");
    }
}

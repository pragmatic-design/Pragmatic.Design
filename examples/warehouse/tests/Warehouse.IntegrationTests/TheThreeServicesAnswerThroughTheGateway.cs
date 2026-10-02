using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     Every service answers through the gateway, and only through it; Stock answers from both
///     of its instances.
/// </summary>
/// <remarks>
///     <para>
///         The request goes to the gateway and nowhere else. Which process answered is read from
///         <c>X-Served-By</c> — host name and instance id, the second new at every start — and compared
///         with what that host says about itself, so "the Orders service answered" is a fact about one
///         process and not about a path.
///     </para>
///     <para>
///         <c>/health</c> is each service's own path: the gateway publishes it under <c>/orders</c> and
///         removes the prefix on the way in, and the Stock route names both instances.
///     </para>
/// </remarks>
[Collection(WarehouseCollection.Name)]
public sealed class TheThreeServicesAnswerThroughTheGateway(WarehouseFixture warehouse)
{
    [Fact]
    public async Task Orders_AnswersItsHealth_ThroughTheGateway()
    {
        using var gateway = SignedGateway();

        using var response = await gateway.GetAsync("/orders/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ServedBy(response).Should().Be(warehouse.Orders.ServedBy);
    }

    [Fact]
    public async Task Shipping_AnswersItsHealth_ThroughTheGateway()
    {
        using var gateway = SignedGateway();

        using var response = await gateway.GetAsync("/shipping/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        ServedBy(response).Should().Be(warehouse.Shipping.ServedBy);
    }

    /// <summary>
    ///     Two requests to the Stock route are answered by the two instances, one each: the route takes
    ///     its backends in turn.
    /// </summary>
    [Fact]
    public async Task Stock_BothInstancesAnswer_ThroughTheGateway()
    {
        using var gateway = SignedGateway();

        var servedBy = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            using var response = await gateway.GetAsync("/warehouse/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            servedBy.Add(ServedBy(response));
        }

        servedBy.Should().BeEquivalentTo([warehouse.StockA.ServedBy, warehouse.StockB.ServedBy],
            $"two instances of one host, and each answered once (A at {warehouse.StockA.Address}, "
            + $"B at {warehouse.StockB.Address})");
    }

    /// <summary>
    ///     The control for the one above: each Stock instance, asked on its own port, names itself — so a
    ///     response through the gateway that names one of them was answered by that one.
    /// </summary>
    [Fact]
    public async Task EachStockInstance_AskedDirectly_NamesItself()
    {
        foreach (var instance in new[] { warehouse.StockA, warehouse.StockB })
        {
            using var direct = new HttpClient { BaseAddress = new Uri(instance.Address) };

            using var response = await direct.GetAsync("/health");

            ServedBy(response).Should().Be(instance.ServedBy, $"asked at {instance.Address}");
        }
    }

    /// <summary>
    ///     The control: a path the gateway publishes nothing under is the gateway's 404, and no service
    ///     saw the request.
    /// </summary>
    [Fact]
    public async Task APathTheGatewayDoesNotPublish_Is404_FromTheGateway()
    {
        using var gateway = warehouse.Gateway.CreateClient();

        using var response = await gateway.GetAsync("/invoices/health");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Headers.Contains("X-Served-By").Should().BeFalse(
            "a service would have said which one it was; the gateway answered on its own");
    }

    /// <summary>
    ///     A gateway client with a valid token: the gateway asks who is calling on every route, health
    ///     included. The role does not matter — <c>/health</c> needs no permission in the service.
    /// </summary>
    private HttpClient SignedGateway()
    {
        var token = warehouse.Orders.Services.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: $"monitor-{Guid.NewGuid():N}", roles: [OrderCalls.Desk]).Token;

        var client = warehouse.Gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string ServedBy(HttpResponseMessage response)
        => response.Headers.TryGetValues("X-Served-By", out var values)
            ? values.Single()
            : throw new InvalidOperationException("The response carries no X-Served-By: no service answered it.");
}

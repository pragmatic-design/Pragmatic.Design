using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Messaging;
using Warehouse.Stock.Contracts.Events;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The calls a Shipping test makes: through the gateway as a shipping clerk, and the picked order that
///     starts a shipment, published on the broker as Stock publishes it.
/// </summary>
internal static class ShippingCalls
{
    public const string Clerk = "shipping-clerk";

    /// <summary>A client through the gateway, as a shipping clerk.</summary>
    public static HttpClient ThroughTheGateway(WarehouseFixture warehouse)
    {
        var token = warehouse.Shipping.Services.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: $"{Clerk}-{Guid.NewGuid():N}", roles: [Clerk]).Token;

        var client = warehouse.Gateway.CreateClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "/shipping/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    ///     Publishes <paramref name="picked" /> from a Stock instance's bus — the process that publishes it
    ///     in a deployment — once Shipping's queue for it is bound: a topic exchange discards what no queue
    ///     is bound to.
    /// </summary>
    public static async Task PublishAsync(WarehouseFixture warehouse, OrderPicked picked)
    {
        await WarehouseWaits.UntilAsync(() => warehouse.Broker.PendingInAsync("order-picked"), pending => pending is not null,
            "Shipping bound its queue for OrderPicked");

        await using var scope = warehouse.StockA.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(picked);
    }

    /// <summary>What <c>GET api/shipments?orderId=</c> answers.</summary>
    public static async Task<JsonElement[]> ShipmentsOfAsync(HttpClient client, Guid orderId)
    {
        using var response = await client.GetAsync($"api/shipments?orderId={orderId}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET api/shipments answered {(int)response.StatusCode}: {body}");

        var root = JsonDocument.Parse(body).RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return [.. rows.EnumerateArray().Select(row => row.Clone())];
    }
}

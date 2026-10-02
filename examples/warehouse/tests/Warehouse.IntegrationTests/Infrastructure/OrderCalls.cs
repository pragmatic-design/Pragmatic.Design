using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;
using Pragmatic.Persistence.Repository;
using Warehouse.Orders;
using Warehouse.Orders.Entities;
using Warehouse.Orders.Enums;
using Warehouse.Orders.Host;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The calls an Orders test makes, through the gateway, with a caller whose token the Orders host signed.
/// </summary>
internal static class OrderCalls
{
    public const string Desk = "order-desk";

    /// <summary>A client through the gateway, as the order desk.</summary>
    public static HttpClient ThroughTheGateway(WarehouseFixture warehouse)
        => As(warehouse.Gateway.CreateClient(), warehouse.Orders, prefix: "/orders/");

    /// <summary>A client to one Orders instance and no other, as the order desk.</summary>
    public static HttpClient ToInstance(ServiceHost<OrdersHost> instance)
        => As(new HttpClient { BaseAddress = new Uri(instance.Address) }, instance, prefix: "/");

    private static HttpClient As(HttpClient client, ServiceHost<OrdersHost> signer, string prefix)
    {
        var token = signer.Services.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: $"{Desk}-{Guid.NewGuid():N}", roles: [Desk]).Token;

        client.BaseAddress = new Uri(client.BaseAddress!, prefix);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Drafts an order for the given lines and answers the response, whatever it is.</summary>
    public static Task<HttpResponseMessage> DraftAsync(HttpClient client, params (string Sku, int Quantity)[] lines)
        => client.PostAsJsonAsync("api/orders", new
        {
            customerReference = $"CUST-{Guid.NewGuid():N}"[..20],
            lines = lines.Select(line => new { sku = line.Sku, quantity = line.Quantity }).ToArray(),
        });

    /// <summary>Drafts an order that is valid, and answers its id.</summary>
    public static async Task<Guid> NewDraftAsync(HttpClient client)
    {
        using var created = await DraftAsync(client, ("SKU-A", 1));
        var body = await created.Content.ReadAsStringAsync();
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST api/orders answered {(int)created.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Drafts an order for <paramref name="quantity" /> of <paramref name="sku" /> and places it: its id, Reserved.</summary>
    public static async Task<Guid> PlacedAsync(HttpClient desk, string sku, int quantity)
    {
        using var created = await DraftAsync(desk, (sku, quantity));
        var body = await created.Content.ReadAsStringAsync();
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"POST api/orders answered {(int)created.StatusCode}: {body}");
        var order = JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();

        using var placed = await desk.PostAsync($"api/orders/{order}/place", null);
        if (!placed.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"placing {order} answered {(int)placed.StatusCode}: {await placed.Content.ReadAsStringAsync()}");

        return order;
    }

    /// <summary>The order's status, as <c>GET api/orders/{id}</c> reads it.</summary>
    public static async Task<string> StatusOfAsync(HttpClient client, Guid id)
        => (await ReadAsync(client, id)).GetProperty("status").GetString() ?? "";

    /// <summary>What <c>GET api/orders/{id}</c> answers.</summary>
    public static async Task<JsonElement> ReadAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"api/orders/{id}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET api/orders/{id} answered {(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    /// <summary>
    ///     Carries an order to <see cref="OrderStatus.Shipped" /> through its own machine, one move at a time.
    /// </summary>
    /// <remarks>
    ///     ⚠️ An arrangement, and the reason it is not HTTP: the moves after <c>Reserved</c> are made by
    ///     messages — the picking and the dispatch — not by a route on Orders. Walking the generated
    ///     <c>TransitionTo</c> keeps the machine's rules in force, where
    ///     writing the column would put the row in a state no move could have reached.
    /// </remarks>
    public static async Task ShipAsync(WarehouseFixture warehouse, Guid id)
    {
        await using var scope = warehouse.Orders.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();

        var order = await scope.ServiceProvider.GetRequiredService<IRepository<Order>>().GetByIdAsync(id)
            ?? throw new InvalidOperationException($"No order {id}");

        OrderStatus[] path =
            [OrderStatus.Placed, OrderStatus.Reserved, OrderStatus.ReadyToPick, OrderStatus.Picked, OrderStatus.Shipped];
        foreach (var next in path)
        {
            var moved = order.TransitionTo(next);
            if (moved.IsFailure)
                throw new InvalidOperationException($"{order.Status} → {next} refused: {moved.Error.Code}");
        }

        await scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(OrdersBoundary)).SaveChangesAsync();
    }
}

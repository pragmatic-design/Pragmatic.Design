using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Identity.Local.Jwt;
using Warehouse.Stock.Host;

namespace Warehouse.IntegrationTests.Infrastructure;

/// <summary>
///     The calls a Stock test makes, with a caller whose token the Stock host signed.
/// </summary>
/// <remarks>
///     The token comes from the host's own <see cref="JwtTokenGenerator" /> and not from a hand-built JWT
///     here: a second copy of the signing and claim-naming rules would keep passing after the first one
///     changed. Both Stock instances share the key, so a token from one is valid on the other — and on
///     whatever the gateway picks.
/// </remarks>
internal static class StockCalls
{
    public const string Manager = "stock-manager";
    public const string Clerk = "stock-clerk";

    /// <summary>A client through the gateway, as the given role.</summary>
    public static HttpClient ThroughTheGateway(WarehouseFixture warehouse, string role)
        => As(warehouse.Gateway.CreateClient(), warehouse.StockA, role, prefix: "/warehouse/");

    /// <summary>A client to one Stock instance and no other, as the given role.</summary>
    public static HttpClient ToInstance(ServiceHost<StockHost> instance, string role)
        => As(new HttpClient { BaseAddress = new Uri(instance.Address) }, instance, role, prefix: "/");

    private static HttpClient As(HttpClient client, ServiceHost<StockHost> signer, string role, string prefix)
    {
        var token = signer.Services.GetRequiredService<JwtTokenGenerator>()
            .Generate(subject: $"{role}-{Guid.NewGuid():N}", roles: [role]).Token;

        client.BaseAddress = new Uri(client.BaseAddress!, prefix);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    ///     A product with a SKU nobody has used, named in English and Italian: its id and its SKU. Its own
    ///     reorder threshold is <paramref name="reorderThreshold" />; 0 leaves it to the default.
    /// </summary>
    public static async Task<(Guid Id, string Sku)> NewProductAsync(
        HttpClient client, string english = "Pallet jack", string italian = "Transpallet", int reorderThreshold = 5)
    {
        var sku = $"SKU-{Guid.NewGuid():N}"[..20];
        using var created = await client.PostAsJsonAsync("api/products", new
        {
            sku,
            name = new Dictionary<string, string> { ["en-US"] = english, ["it-IT"] = italian },
            reorderThreshold,
        });
        return (await IdOfAsync(created), sku);
    }

    /// <summary>A location with a code nobody has used; its id.</summary>
    public static async Task<Guid> NewLocationAsync(HttpClient client)
    {
        using var created = await client.PostAsJsonAsync("api/locations", new
        {
            code = $"L-{Guid.NewGuid():N}"[..18],
            description = "A bay the suite made",
        });
        return await IdOfAsync(created);
    }

    /// <summary>A product with this much on hand at one new location, through one instance: its id and its SKU.</summary>
    public static async Task<(Guid Id, string Sku)> InStockAsync(
        ServiceHost<StockHost> instance, int quantity, int reorderThreshold = 5)
    {
        using var manager = ToInstance(instance, Manager);
        var product = await NewProductAsync(manager, reorderThreshold: reorderThreshold);
        var location = await NewLocationAsync(manager);

        using var received = await manager.PostAsJsonAsync("api/levels/receipts",
            new { productId = product.Id, locationId = location, quantity });
        await IdOfAsync(received);

        return product;
    }

    /// <summary>What <c>GET api/levels</c> answers for one product: one row per location.</summary>
    public static async Task<JsonElement[]> LevelsOfAsync(HttpClient client, Guid productId)
    {
        using var response = await client.GetAsync($"api/levels?productId={productId}");
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"GET api/levels answered {(int)response.StatusCode}: {body}");

        var root = JsonDocument.Parse(body).RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return [.. rows.EnumerateArray().Select(row => row.Clone())];
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage created)
    {
        var body = await created.Content.ReadAsStringAsync();
        if (!created.IsSuccessStatusCode)
            throw new InvalidOperationException($"{created.RequestMessage?.RequestUri} answered {(int)created.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.GetProperty("id").GetGuid();
    }
}

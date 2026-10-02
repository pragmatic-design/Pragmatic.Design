using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     The catalogue, through the gateway: a SKU is unique, and a product's name answers in the
///     language of the request.
/// </summary>
[Collection(WarehouseCollection.Name)]
public sealed class TheCatalogueThroughTheGateway(WarehouseFixture warehouse)
{
    [Fact]
    public async Task ASecondProductWithTheSameSku_Is409_NamingTheField()
    {
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);
        var sku = $"DUP-{Guid.NewGuid():N}"[..20];
        var product = new { sku, name = new Dictionary<string, string> { ["en-US"] = "Crate" } };

        using var first = await manager.PostAsJsonAsync("api/products", product);
        first.StatusCode.Should().Be(HttpStatusCode.Created, await first.Content.ReadAsStringAsync());

        using var second = await manager.PostAsJsonAsync("api/products", product);

        var body = await second.Content.ReadAsStringAsync();
        second.StatusCode.Should().Be(HttpStatusCode.Conflict, body);
        body.Should().Contain("Sku", "the conflict names the field the unique index covers");
    }

    [Theory]
    [InlineData("it-IT", "Transpallet")]
    [InlineData("en-US", "Pallet jack")]
    public async Task TheName_AnswersInTheLanguageOfTheRequest(string language, string expected)
    {
        using var manager = StockCalls.ThroughTheGateway(warehouse, StockCalls.Manager);
        var (_, sku) = await StockCalls.NewProductAsync(manager, english: "Pallet jack", italian: "Transpallet");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/products?sku={sku}");
        request.Headers.AcceptLanguage.ParseAdd(language);
        using var response = await manager.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        var product = Items(body).Should().ContainSingle().Subject;
        product.GetProperty("name").GetString().Should().Be(expected);
    }

    private static JsonElement[] Items(string body)
    {
        var root = JsonDocument.Parse(body).RootElement;
        var rows = root.ValueKind == JsonValueKind.Array ? root : root.GetProperty("items");
        return [.. rows.EnumerateArray().Select(row => row.Clone())];
    }
}

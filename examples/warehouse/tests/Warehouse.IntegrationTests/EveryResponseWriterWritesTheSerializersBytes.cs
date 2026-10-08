using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Examples.Testing;
using Pragmatic.Testing.Assertions;
using Warehouse.IntegrationTests.Infrastructure;

namespace Warehouse.IntegrationTests;

/// <summary>
///     Every response type the three services answer with through a generated writer is written with the bytes the
///     serializer writes for it under that service's own running host options.
/// </summary>
/// <remarks>The writers are found, not listed; the first assertion of each is the control that any were.</remarks>
[Collection(WarehouseCollection.Name)]
public sealed class EveryResponseWriterWritesTheSerializersBytes(WarehouseFixture warehouse)
{
    [Fact]
    public void Orders_EveryGeneratedWriter_WritesTheSerializersBytes()
        => Conforms(warehouse.Orders.Services, typeof(Warehouse.Orders.Dtos.OrderDto).Assembly);

    [Fact]
    public void Stock_EveryGeneratedWriter_WritesTheSerializersBytes()
        => Conforms(warehouse.StockA.Services, typeof(Warehouse.Stock.Dtos.ProductDto).Assembly);

    [Fact]
    public void Shipping_EveryGeneratedWriter_WritesTheSerializersBytes()
        => Conforms(warehouse.Shipping.Services, typeof(Warehouse.Shipping.Dtos.ShipmentDto).Assembly);

    private static void Conforms(IServiceProvider host, System.Reflection.Assembly module)
    {
        var options = host.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        ResponseWriterConformance.Used([module], options).Should().NotBeEmpty(
            "the module answers with types the generator writes, and the host's converters claim none of what some of them write");
        ResponseWriterConformance.Mismatches([module], options).Should().BeEmpty();
    }
}

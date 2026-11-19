using System.Text.Json;
using Pragmatic.Integration.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>
///     A C# 15 union an operation answers with is written as the case it holds, the way System.Text.Json 11 writes a union (#838, item 3).
/// </summary>
/// <remarks>
///     The control is System.Text.Json itself, with its reflection resolver, on the same value: STJ 11 writes a union as its active case,
///     with no wrapper. The response goes through what the host generated for the type, so this measures that path, not STJ's.
/// </remarks>
public sealed class AUnionIsWrittenAsItsCaseTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private IntegrationTestFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new IntegrationTestFactory();
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("courier")]
    [InlineData("pickup")]
    public async Task TheResponse_IsTheActiveCase_AsSystemTextJsonWritesIt(string mode)
    {
        var response = await _client.GetAsync($"/api/shipments/quote?mode={mode}");
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());

        var expected = mode == "pickup" ? new Shipment(new Pickup("Milano Centro")) : new Shipment(new Courier("DHL", 2));
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetRawText();

        body.Should().Be(JsonSerializer.Serialize(expected, Web), "the active case, with no wrapper and no 'value' property");
    }

    /// <summary>
    ///     The generated JSON context describes the union itself, with no reflection behind it: what an AOT application has. It writes
    ///     each case, and a default union, as System.Text.Json's reflection resolver does.
    /// </summary>
    [Fact]
    public void TheGeneratedContext_WritesTheUnionAsReflectionDoes()
    {
        var context = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = Generated.PragmaticJsonContext.Default };

        context.TryGetTypeInfo(typeof(Shipment), out var info).Should().BeTrue("the context covers the union an operation answers with");
        info!.Kind.Should().Be(System.Text.Json.Serialization.Metadata.JsonTypeInfoKind.Union);

        foreach (var shipment in new[] { new Shipment(new Courier("DHL", 2)), new Shipment(new Pickup("Milano Centro")), default })
            JsonSerializer.Serialize(shipment, context).Should().Be(JsonSerializer.Serialize(shipment, Web));
    }
}

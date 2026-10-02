using System.Net;
using System.Text.Json;
using Conformance.Sales;
using Conformance.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Actions.Mutation;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     What a Create returns, for each of the three shapes of <c>MutationReturnType</c>, and its 201's
///     <c>Location</c>.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ Each shape is pinned to what the wire really carries: a declared default that the wire
///         contradicts, or alternative shapes declared and never wired, would leave the attribute, its
///         documentation and <c>PRAG0417</c> describing something that does not happen.
///     </para>
///     <para>
///         The <c>Location</c> exists only where a <c>Single</c> read answers on the Create's route plus
///         the id: it is the only address the generator knows without guessing it, and every case here
///         <b>follows</b> it, because a well-formed header can still name a route nobody serves.
///     </para>
/// </remarks>
public class TheCreatedResponse(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <summary>
    ///     With neither <c>ReturnType</c> nor <c>[ReturnsDto]</c>, a Create answers with the id of the
    ///     written row: never with the entity.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The attribute's default (<c>Entity</c>) governs what the boundary member returns in-process;
    ///     on the wire the entity goes out only if declared.
    /// </remarks>
    [Fact]
    public async Task ACreateThatDeclaresNothing_AnswersTheId_NotTheEntity()
    {
        var code = Code();
        var response = await PostAsync("/api/shipments", new { trackingCode = code, carrier = "ParcelCo" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(response);

        PropertyNames(body).Should().BeEquivalentTo(["id"], "the entity is not the default response");
        var id = body.GetProperty("id").GetGuid();
        await FollowsToTheCreatedRowAsync(response, "/api/shipments", id, code);
    }

    /// <summary>The control: in-process, the boundary member of that same Create still returns the entity.</summary>
    [Fact]
    public async Task TheBoundaryMemberOfACreateThatDeclaresNothing_StillReturnsTheEntity()
    {
        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesActions>();

        var code = Code();
        object? returned = (await sales.CreateShipment(code, "ParcelCo")).Value;

        returned.Should().BeOfType<Conformance.Sales.Entities.Shipment>("in-process the attribute's default stays Entity");
        new MutationAttribute().ReturnType.Should().Be(MutationReturnType.Entity);
    }

    /// <summary><c>Id</c>: 201 with <c>{"id": …}</c> and nothing else, and a <c>Location</c> that can be followed.</summary>
    [Fact]
    public async Task ReturnTypeId_AnswersTheIdAlone()
    {
        var code = Code();
        var response = await PostAsync("/api/shipment-ids", new { trackingCode = code, carrier = "ParcelCo" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(response);

        PropertyNames(body).Should().BeEquivalentTo(["id"],
            "the Id shape carries the technical key and nothing else: neither the domain key nor the rest of the entity");
        var id = body.GetProperty("id").GetGuid();
        id.Should().NotBe(Guid.Empty);

        await FollowsToTheCreatedRowAsync(response, "/api/shipment-ids", id, code);
    }

    /// <summary><c>LogicalKey</c>: 201 with the domain key under its wire name.</summary>
    /// <remarks>
    ///     It is also the <c>Location</c> control: no read answers on <c>api/shipment-keys/{id}</c>, and the
    ///     201 does not invent one.
    /// </remarks>
    [Fact]
    public async Task ReturnTypeLogicalKey_AnswersTheDomainKey_AndNoLocationWithoutARead()
    {
        var code = Code();
        var response = await PostAsync("/api/shipment-keys", new { trackingCode = code, carrier = "ParcelCo" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await ReadAsync(response);

        PropertyNames(body).Should().BeEquivalentTo(["trackingCode"],
            "the LogicalKey shape carries the parts of the [LogicKey], under their wire name");
        body.GetProperty("trackingCode").GetString().Should().Be(code);

        response.Headers.Location.Should().BeNull(
            "without a read on the Create's route plus the id there is no address to name");
    }

    /// <summary>The boundary member of the <c>Id</c> shape returns the key, not the entity.</summary>
    [Fact]
    public async Task TheBoundaryMemberOfAnIdCreate_ReturnsTheKey()
    {
        await using var scope = Services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<ISalesActions>();

        var code = Code();
        // Through `object`, so the case compiles whatever the member returns and fails on what it is.
        object? returned = (await sales.CreateShipmentReturningId(code, "ParcelCo")).Value;

        var created = returned.Should().BeOfType<Guid>(
            "the Id shape declares that the operation returns the key, not only that the wire carries it").Which;
        created.Should().NotBe(Guid.Empty);
        var read = await Client.GetAsync($"/api/shipment-ids/{created}");
        (await ReadAsync(read)).GetProperty("trackingCode").GetString().Should().Be(code,
            "the key returned is the written row's");
    }

    private async Task FollowsToTheCreatedRowAsync(
        HttpResponseMessage created, string createRoute, Guid id, string code)
    {
        created.Headers.Location.Should().NotBeNull(
            "a Single read answers on the Create's route plus the id, and the 201 names it");
        created.Headers.Location!.OriginalString.Should().Be($"{createRoute}/{id}");

        var followed = await Client.GetAsync(created.Headers.Location);
        var row = await ReadAsync(followed);
        row.GetProperty("trackingCode").GetString().Should().Be(code,
            "the address leads to the row just written, not to any route");
    }

    private static List<string> PropertyNames(JsonElement body)
        => body.EnumerateObject().Select(p => p.Name).ToList();

    private static string Code() => $"TRK-{Guid.NewGuid():N}"[..16];
}

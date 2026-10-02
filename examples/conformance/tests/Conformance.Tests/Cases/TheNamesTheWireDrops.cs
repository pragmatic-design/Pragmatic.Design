using System.Text.Json;
using Conformance.Tests.Infrastructure;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     What the serializer strips is not in the contract — and the two halves are read side by side.
/// </summary>
/// <remarks>
///     <para>
///         The runtime removes <c>PersistenceId</c>, <c>RowVersion</c>, <c>TenantId</c>, <c>OwnerId</c> and
///         <c>AccessScopes</c> from every serialized type: they are tracking, tenancy and the shape of
///         authorization, and reading who owns a row and which scopes make it visible is reconnaissance for
///         an escalation. The OpenAPI document comes from the DTO's shape, and must not announce them
///         either. Here <c>PersistenceId</c> is measured, the only one of the five <c>Order</c> has.
///     </para>
///     <para>
///         ⚠️ <b>Neither end is wrong on its own.</b> Whoever strips, strips; whoever publishes, publishes;
///         the defect exists only between the two — a client generated from the document would read a
///         silent default in place of a field no response carries. That is why the case reads <b>the
///         response first</b> and then the document: measuring the document first and looking for a
///         response that confirms it is the way to write a test that always passes.
///     </para>
///     <para>
///         <c>OrderWireShapeDto</c> is the control shape, declaring a reserved name on purpose.
///     </para>
/// </remarks>
public class TheNamesTheWireDrops(PostgresFixture fixture) : E2ETestBase(fixture)
{
    /// <remarks>
    ///     Only one, and the reason is in <c>OrderWireShapeDto</c>'s notes: the other four names do not
    ///     exist on <c>Order</c> and the mapping refuses to declare them (<c>PRAG0303</c>). That the list is
    ///     a single one for all five is kept by the generator test
    ///     (<c>ReservedWireNamesAreNotPublishedTests</c>), where the DTO maps from no entity.
    /// </remarks>
    private static readonly string[] Dropped = ["persistenceId"];

    private async Task<JsonElement> AnOrderReadAsWireShapeAsync()
    {
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference = $"ORD-{Guid.NewGuid():N}"[..12],
            lines = new[] { new { product = "bread", quantity = 2 } },
        }));

        var id = created.GetProperty("id").GetGuid();

        return await ReadAsync(await Client.GetAsync($"api/orders/{id}/wire-shape"));
    }

    /// <summary>The wire: the response carries the published property and none of the others.</summary>
    [Fact]
    public async Task TheResponse_CarriesWhatIsPublished_AndNothingReserved()
    {
        var body = await AnOrderReadAsWireShapeAsync();

        var names = body.EnumerateObject().Select(p => p.Name).ToList();

        names.Should().Contain("reference",
            "without a property that arrives, «ownerId does not arrive» would be true of an empty response too");

        foreach (var dropped in Dropped)
            names.Should().NotContain(dropped, $"the serializer removes {dropped}");
    }

    /// <summary>The document: the same type's schema says the same thing.</summary>
    /// <remarks>
    ///     The schema is read from the <b>served</b> document, not from the generated constant: between the
    ///     two sits the wiring, and the contract that matters is the one a client downloads.
    /// </remarks>
    [Fact]
    public async Task TheDocument_PublishesTheSameShapeAsTheWire()
    {
        var body = await AnOrderReadAsWireShapeAsync();
        var onTheWire = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();

        var document = await ReadAsync(await Client.GetAsync("/openapi/v1.json"));

        var schema = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("OrderWireShapeDto")
            .GetProperty("properties");

        var published = schema.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToList();

        published.Should().Equal(onTheWire,
            "the document is what a client is generated from: announcing a field the response does not "
            + "carry makes it read a silent default, and omitting one it carries makes it invisible");
    }
}

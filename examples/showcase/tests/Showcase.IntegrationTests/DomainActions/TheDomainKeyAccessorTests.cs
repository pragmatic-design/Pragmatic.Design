using System.Net;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.DomainActions;

/// <summary>
///     An operation reads a row by its domain key, through the contract it already injects.
/// </summary>
/// <remarks>
///     <para>
///         <c>Amenity.Name</c> is a <c>[LogicKey]</c>, and <c>GetAmenityByNameAction</c> declares
///         <c>[LoadEntity&lt;Amenity&gt;(nameof(Name), By = nameof(Amenity.Name))]</c>: its invoker reads through
///         <c>IReadRepository&lt;Amenity&gt;.GetByNameAsync</c> — the extension generated beside the <c>ByName</c>
///         specification — and answers the 404. No concrete repository, no <c>IServiceProvider</c>, no lookup
///         written by hand.
///     </para>
///     <para>
///         ⚠️ The accessor is on the read contract, not only on the concrete <c>Amenity.Repository</c>,
///         which an action cannot ask for (PRAG0419). Nothing else in the Showcase or conformance calls
///         a <c>GetBy…Async</c> other than <c>GetByIdAsync</c>, so without this case the generated
///         member would have no caller to fail: this case exists so it has one.
///     </para>
/// </remarks>
public class TheDomainKeyAccessorTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnOperation_ReadsByTheDomainKey_ThroughTheReadContract()
    {
        var name = $"Key-{Guid.NewGuid():N}"[..20];
        var created = await PostAsync<JsonElement>("/api/amenities", new { name, icon = "wifi" });
        var id = created.GetProperty("id").GetGuid();

        var found = await GetAsync<JsonElement>($"/api/amenities/by-name/{name}");

        found.GetProperty("id").GetGuid().Should().Be(id,
            "the row reached by its domain key is the row the key names");
        found.GetProperty("name").GetString().Should().Be(name);
    }

    /// <summary>The control: a key nobody carries finds nothing.</summary>
    /// <remarks>
    ///     Without it, "the accessor returns the amenity" is satisfied by an accessor that ignores its
    ///     argument and returns the first row — which is what a predicate written twice, and wrong in
    ///     one of the two copies, would do.
    /// </remarks>
    [Fact]
    public async Task AKeyNobodyCarries_IsNotFound()
    {
        var response = await Client.GetAsync($"/api/amenities/by-name/Key-{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

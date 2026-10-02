using Conformance.Tests.Infrastructure;
using Xunit;

namespace Conformance.Tests.Cases;

/// <summary>
///     A group's prefix, and the composition when groups nest.
/// </summary>
/// <remarks>
///     <para>
///         Three reads of the same entity, published in three places: outside every group, inside
///         <c>OrdersGroup</c>, and inside <c>ArchivedOrdersGroup</c>, which in turn sits in
///         <c>OrdersGroup</c>. The route the last two declare is <c>/{id}</c> in both cases: the rest of the
///         address comes from the groups.
///     </para>
///     <para>
///         ⚠️ <b>Nesting is exercised here.</b> A mechanism nobody calls is not verified: it is only written.
///     </para>
///     <para>
///         ⚠️ The control case is the <b>first</b> test: the same read without a group. If the prefix stopped
///         being applied, the two grouped routes would give 404 and that one would stay green — a different
///         diagnosis from «the query no longer works», where all three would fall.
///     </para>
/// </remarks>
public class TheGroupPrefix(PostgresFixture fixture) : E2ETestBase(fixture)
{
    private async Task<(Guid Id, string Reference)> AnOrderAsync()
    {
        var reference = $"ORD-{Guid.NewGuid():N}"[..12];
        var created = await ReadAsync(await PostAsync("/api/orders", new
        {
            reference,
            lines = Array.Empty<object>(),
        }));

        return (created.GetProperty("id").GetGuid(), reference);
    }

    [Fact]
    public async Task TheControl_NoGroupMeansTheRouteIsWhatItSays()
    {
        var (id, reference) = await AnOrderAsync();

        var response = await Client.GetAsync($"/api/orders/{id}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(reference, (await ReadAsync(response)).GetProperty("reference").GetString());
    }

    [Fact]
    public async Task AGroupPutsItsPrefixInFrontOfTheRoute()
    {
        var (id, reference) = await AnOrderAsync();

        // The query declares "/{id}"; "/api/conformance/orders" is the group's.
        var response = await Client.GetAsync($"/api/conformance/orders/{id}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(reference, (await ReadAsync(response)).GetProperty("reference").GetString());
    }

    [Fact]
    public async Task ANestedGroupComposesBothPrefixes()
    {
        var (id, reference) = await AnOrderAsync();

        // No declaration writes this address in full: "/api/conformance/orders" comes from OrdersGroup,
        // "/archived" from ArchivedOrdersGroup, "/{id}" from the query.
        var response = await Client.GetAsync($"/api/conformance/orders/archived/{id}");

        response.EnsureSuccessStatusCode();
        Assert.Equal(reference, (await ReadAsync(response)).GetProperty("reference").GetString());
    }

    [Fact]
    public async Task TheUngroupedRouteIsNotAlsoPublishedUnderThePrefix()
    {
        var (id, _) = await AnOrderAsync();

        // The group adds an address, it does not duplicate one: if the prefix were applied to everything,
        // this would answer instead of giving 404, and the three tests above would pass anyway.
        var response = await Client.GetAsync($"/api/conformance/api/orders/{id}");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Showcase.IntegrationTests.Infrastructure;
using Xunit;

namespace Showcase.IntegrationTests.Queries;

/// <summary>
///     A declared visibility rule, and the one read allowed past it.
/// </summary>
/// <remarks>
///     <para>
///         <c>Property</c> carries <c>[VisibleWhen&lt;ActiveOnly&gt;]</c>, so the rule is declared once.
///         As a line repeated at each call site — <c>.Where(PropertySpecifications.IsActive())</c> — any
///         read that did not remember it would list deactivated properties.
///     </para>
///     <para>
///         <c>SearchDeactivatedPropertiesQuery</c> lifts that one rule with
///         <c>[WithoutFilter&lt;ActiveOnly&gt;]</c>, and declares the permission that makes it
///         legitimate. <c>PRAG0719</c> refuses the attribute without one — verified by taking the
///         permission away, which turns the build red.
///     </para>
/// </remarks>
public class VisibilityRuleTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(Guid Id, string Name)> CreatePropertyAsync()
    {
        var name = $"VisProp-{Guid.NewGuid():N}"[..20];
        var property = await PostAsync<JsonElement>("/api/properties", new
        {
            code = $"VIS-{Guid.NewGuid():N}"[..12],
            name,
            city = "Rome",
            country = "IT",
            starRating = 3
        });

        return (property.GetProperty("id").GetGuid(), name);
    }

    private async Task DeactivateAsync(Guid id)
    {
        var response = await Client.PutAsJsonAsync($"/api/properties/{id}", new { id, isActive = false });
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
    }

    private static bool Contains(JsonElement page, Guid id)
        => page.GetProperty("items").EnumerateArray()
            .Any(item => item.GetProperty("id").GetGuid() == id);

    /// <summary>
    ///     Deactivating a property takes it out of the ordinary search.
    /// </summary>
    /// <remarks>
    ///     The first assertion is the control and has to come first: while it is active the search
    ///     finds it, so its later absence is the rule and not a property that was never there.
    /// </remarks>
    [Fact]
    public async Task ADeactivatedProperty_LeavesTheOrdinarySearch()
    {
        var (id, name) = await CreatePropertyAsync();

        var before = await Client.GetFromJsonAsync<JsonElement>(
            $"/api/properties/search?name={name}&pageSize=50", JsonOptions);
        Contains(before, id).Should().BeTrue("it is active, so the catalogue lists it");

        await DeactivateAsync(id);

        var after = await Client.GetFromJsonAsync<JsonElement>(
            $"/api/properties/search?name={name}&pageSize=50", JsonOptions);
        Contains(after, id).Should().BeFalse(
            "[VisibleWhen<ActiveOnly>] takes it out of every read that does not lift the rule");
    }

    /// <summary>
    ///     The one read that is allowed past it finds it.
    /// </summary>
    /// <remarks>
    ///     Together with the test above this is the whole feature: the same row, hidden from one read
    ///     and visible to another, with nothing written at either call site to make it so.
    /// </remarks>
    [Fact]
    public async Task TheAdminRead_FindsWhatTheRuleHides()
    {
        var (id, name) = await CreatePropertyAsync();
        await DeactivateAsync(id);

        var response = await CreateClientWithPermissions(
                "catalog.property.read", "catalog.property.update")
            .GetAsync($"/api/properties/deactivated?name={name}&pageSize=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var page = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        Contains(page, id).Should().BeTrue("the query lifts ActiveOnly, and declares the permission");
    }

    /// <summary>
    ///     A hidden row is hidden from the writes too — which is why management lifts the rule.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The trap this feature carries, and it is not obvious from the declaration. An update
    ///         loads the entity through the same filtered query a read does, so declaring
    ///         <c>[VisibleWhen&lt;ActiveOnly&gt;]</c> and nothing else made a deactivated property
    ///         unreachable by everything: measured <c>read=404 update=404 delete=404</c>. Deactivating
    ///         was irreversible — there was no way back in to set the flag again.
    ///     </para>
    ///     <para>
    ///         So the management mutations carry <c>[WithoutFilter&lt;ActiveOnly&gt;]</c> beside the
    ///         permission they already required. The ordinary read stays blind; whoever may change the
    ///         catalogue may reach what it hides.
    ///     </para>
    /// </remarks>
    [Fact]
    public async Task ADeactivatedProperty_CanStillBeManagedAndReactivated()
    {
        var (id, name) = await CreatePropertyAsync();
        await DeactivateAsync(id);

        var reactivate = await Client.PutAsJsonAsync($"/api/properties/{id}", new { id, isActive = true });
        reactivate.IsSuccessStatusCode.Should().BeTrue(
            await reactivate.Content.ReadAsStringAsync());

        var search = await Client.GetFromJsonAsync<JsonElement>(
            $"/api/properties/search?name={name}&pageSize=50", JsonOptions);
        Contains(search, id).Should().BeTrue("it is active again, so the catalogue lists it again");
    }

    /// <summary>
    ///     Lifting one rule is not lifting the rest.
    /// </summary>
    /// <remarks>
    ///     <c>[WithoutFilter&lt;ActiveOnly&gt;]</c> names a filter, not the entity: soft-delete stays.
    ///     Naming the entity instead would have lifted everything, which is the older and blunter form
    ///     of the same attribute — and the reason naming the rule is worth the class it costs.
    /// </remarks>
    [Fact]
    public async Task TheAdminRead_StillDoesNotSeeDeletedRows()
    {
        var (id, name) = await CreatePropertyAsync();
        await DeactivateAsync(id);

        var client = CreateClientWithPermissions(
            "catalog.property.read", "catalog.property.update", "catalog.property.delete");

        (await client.DeleteAsync($"/api/properties/{id}")).IsSuccessStatusCode.Should().BeTrue();

        var page = await client.GetFromJsonAsync<JsonElement>(
            $"/api/properties/deactivated?name={name}&pageSize=50", JsonOptions);

        Contains(page, id).Should().BeFalse("only ActiveOnly was lifted — the row is soft-deleted");
    }

}

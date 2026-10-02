using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Persistence.Repository;
using Pragmatic.Testing.Assertions;
using Showcase.Catalog.Entities;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Endpoints;

/// <summary>
///     <c>[PatchIgnore]</c>: the half of a patch that is about what it may <b>not</b> do.
/// </summary>
/// <remarks>
///     <para>
///         A patch's shape is derived from the entity, so every settable property is patchable
///         unless something says otherwise. <c>Amenity.Name</c> is the <c>[LogicKey]</c> — what the
///         catalogue is keyed on and what autocomplete resolves against — so renaming one is a
///         decision that goes through the full update, not a field corrected in passing.
///     </para>
///     <para>
///         ⚠️ <b>The field is refused, not ignored</b> — measured, and the opposite of what the
///         attribute's name suggests. The declaration removes the property from the generated patch
///         <em>and</em> from its JSON converter, so a body carrying it does not bind and the caller
///         gets <b>400</b>. That is the better of the two contracts, and the one the attribute's own
///         motivation asks for: the published shape and the refusal now agree, where before an
///         endpoint refused a field its contract went on advertising.
///     </para>
/// </remarks>
public class WhatAPatchMayNotTouchTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task APatchNamingTheLogicKey_IsRefused_AndTheNameSurvives()
    {
        var original = $"Ignore-{Guid.NewGuid():N}"[..20];
        var amenityId = await AnAmenityAsync(original);

        var refused = await Client.PatchAsJsonAsync(
            $"/api/amenities/{amenityId}",
            new { patch = new { name = "Renamed by a patch", iconName = "spa" } },
            JsonOptions);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "the generated patch has no Name member, so a body that carries one does not bind");

        var after = await StoredAsync(amenityId);

        after.Name.Should().Be(original);
        after.IconName.Should().Be("pool",
            "nothing was applied: a refused body is refused whole, so the icon in it did not land "
            + "either");
    }

    /// <summary>
    ///     The control: the patch still patches.
    /// </summary>
    /// <remarks>
    ///     Without it, "the rename was refused" is satisfied by a patch endpoint that refuses
    ///     everything — which is what removing the wrong property, or a converter that stopped
    ///     binding, would look like.
    /// </remarks>
    [Fact]
    public async Task TheSamePatchWithoutIt_Applies()
    {
        var original = $"Allow-{Guid.NewGuid():N}"[..20];
        var amenityId = await AnAmenityAsync(original);

        var response = await Client.PatchAsJsonAsync(
            $"/api/amenities/{amenityId}",
            new { patch = new { iconName = "spa" } },
            JsonOptions);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "{0}", await response.Content.ReadAsStringAsync());

        var after = await StoredAsync(amenityId);

        after.IconName.Should().Be("spa");
        after.Name.Should().Be(original,
            "and the name is untouched here too, because nobody asked for it");
    }

    private async Task<Guid> AnAmenityAsync(string name)
    {
        // iconName, not icon: the create mutation's property is IconName, and a member the body
        // does not name simply stays at its default — which is how the amenity next door was
        // created with no icon at all.
        var created = await PostAsync<JsonElement>("/api/amenities", new { name, iconName = "pool" });
        return created.GetProperty("id").GetGuid();
    }

    /// <summary>
    ///     The row itself. The search route is <c>[Cacheable]</c> for ten minutes, so reading back
    ///     through it would be asking the cache what the patch did.
    /// </summary>
    private async Task<Amenity> StoredAsync(Guid amenityId)
    {
        using var scope = Services.CreateScope();

        var amenity = await scope.ServiceProvider
            .GetRequiredService<IReadRepository<Amenity>>()
            .GetByIdAsync(amenityId);

        amenity.Should().NotBeNull();
        return amenity!;
    }
}

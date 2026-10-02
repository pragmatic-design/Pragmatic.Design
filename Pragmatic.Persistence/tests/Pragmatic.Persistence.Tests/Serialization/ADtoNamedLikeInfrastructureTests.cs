using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Serialization;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Persistence.Tests.Serialization;

/// <summary>
///     A response type of the application's own keeps the members it declares, even where their names
///     match the framework's change tracking.
/// </summary>
/// <remarks>
///     <para>
///         The strip matched by name on <b>every</b> object type, so a DTO with a member called
///         <c>ModifiedProperties</c> lost it on the wire and in the document, in silence. The Showcase
///         `PatchAmenityResult(Guid Id, List&lt;string&gt; ModifiedProperties)` answered `{"id": …}`, and
///         the generated client had only `Id`.
///     </para>
///     <para>
///         ⚠️ The change-tracking half of the list is what narrows — it exists for
///         <see cref="IChangeTracking" /> and <c>IHasDomainEvents</c>, which an application's DTO
///         does not implement. The other half (<c>TenantId</c>, <c>OwnerId</c>, <c>AccessScopes</c>,
///         <c>RowVersion</c>, <c>PersistenceId</c>) is still stripped everywhere: it is there so that
///         ownership and visibility scopes cannot leak through a projection that carried them by
///         accident, and that is a property of the response, not of the type that produced it.
///     </para>
/// </remarks>
public sealed class ADtoNamedLikeInfrastructureTests
{
    private sealed class PatchResult
    {
        public Guid Id { get; set; }
        public List<string> ModifiedProperties { get; set; } = [];
        public bool IsNew { get; set; }
    }

    private sealed class TrackedEntity : IChangeTracking
    {
        public string Name { get; set; } = "";
        public IReadOnlySet<string> ModifiedProperties => new HashSet<string> { "Name" };
        public IReadOnlySet<string> CollectionsModified => new HashSet<string>();
        public bool IsNew { get; set; }

        public void ResetModifiedProperties() { }
    }

    private sealed class ProjectionWithOwnership
    {
        public string Name { get; set; } = "";
        public Guid OwnerId { get; set; }
        public List<string> AccessScopes { get; set; } = [];
    }

    private static string Serialize<T>(T value)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
                .WithAddedModifier(EntityJsonModifier.ExcludeInfrastructureProperties)
        };

        return JsonSerializer.Serialize(value, options);
    }

    [Fact]
    public void ADtoOfTheApplication_KeepsAMemberNamedLikeChangeTracking()
    {
        var json = Serialize(new PatchResult { Id = Guid.NewGuid(), ModifiedProperties = { "Name" }, IsNew = true });

        json.Should().Contain("ModifiedProperties", "the DTO declares it, and nothing else writes it");
        json.Should().Contain("IsNew");
        json.Should().Contain("Id");
    }

    /// <summary>The control: on a type that really tracks changes, the same names are still stripped.</summary>
    [Fact]
    public void AnEntityTrackingItsChanges_StillLosesThem()
    {
        var json = Serialize(new TrackedEntity { Name = "widget", IsNew = true });

        json.Should().Contain("Name");
        json.Should().NotContain("ModifiedProperties", "the framework writes it, and a response is not the place for it");
        json.Should().NotContain("CollectionsModified");
        json.Should().NotContain("IsNew");
    }

    /// <summary>
    ///     The control that matters most: narrowing the change-tracking half does not open the half that
    ///     exists so ownership cannot leak.
    /// </summary>
    [Fact]
    public void AProjectionCarryingOwnership_StillLosesIt()
    {
        var json = Serialize(new ProjectionWithOwnership { Name = "widget", OwnerId = Guid.NewGuid(), AccessScopes = { "user:1" } });

        json.Should().Contain("Name");
        json.Should().NotContain("OwnerId", "ownership must not leak to clients, whatever type carries it");
        json.Should().NotContain("AccessScopes");
    }
}

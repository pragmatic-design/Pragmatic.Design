using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class OwnershipPropertyTemplateTests
{
    [Fact]
    public void OwnedEntity_GeneratesOwnerIdProperty()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("public string OwnerId { get; private set; } = string.Empty;");
    }

    [Fact]
    public void OwnedEntity_GeneratesSetOwnerIdMethod()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("internal void SetOwnerId(string ownerId)");
        source.Should().Contain("OwnerId = ownerId;");
    }

    /// <summary>
    ///     The entity is assignable, which is how it is also an <c>IOwnedEntity</c>.
    /// </summary>
    /// <remarks>
    ///     <c>IOwnershipAssignable</c> derives from <c>IOwnedEntity</c>, so naming it covers both. It is
    ///     what lets the persistence layer stamp an owner on a row inserted without one — the case an
    ///     action writing through a repository produces, where no mutation invoker runs and the row
    ///     would otherwise reach the database unowned and then be hidden from everyone by its own filter.
    /// </remarks>
    [Fact]
    public void OwnedEntity_ImplementsIOwnershipAssignableInterface()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("global::Pragmatic.Persistence.Entity.IOwnershipAssignable");
    }

    /// <summary>
    ///     And assigning is reachable only through the interface.
    /// </summary>
    /// <remarks>
    ///     Explicit implementation on purpose: a settable <c>OwnerId</c> would let any caller reassign
    ///     ownership, which is the single thing ownership exists to prevent.
    /// </remarks>
    [Fact]
    public void OwnedEntity_ImplementsAssignOwnerExplicitly()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain(
            "void global::Pragmatic.Persistence.Entity.IOwnershipAssignable.AssignOwner(string ownerId)");
    }

    [Fact]
    public void OwnedEntity_WithManualOwnerId_SkipsGeneration()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Reservation",
            FullTypeName = "Booking.Reservation",
            Namespace = "Booking",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            HasManualOwnedEntityProps = true
        };

        var template = new OwnershipPropertyTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void OwnedEntity_WithManualInterface_DoesNotAddInterfaceAgain()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Reservation",
            FullTypeName = "Booking.Reservation",
            Namespace = "Booking",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            HasManualOwnedEntityInterface = true
        };

        var source = Render(model);

        source.Should().Contain("OwnerId");
        source.Should().NotContain(": global::Pragmatic.Persistence.Entity.IOwnedEntity");
    }

    [Fact]
    public void NonOwnedEntity_SkipsGeneration()
    {
        var model = BuildModel("Sales", "Order", isOwnedEntity: false);

        var template = new OwnershipPropertyTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void OwnedEntity_HintName_FollowsConvention()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var template = new OwnershipPropertyTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Reservation");
        artifact.HintName.Should().Contain("Ownership");
    }

    [Fact]
    public void NeedsTraitGeneration_IncludesOwnedEntity()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Reservation",
            FullTypeName = "Booking.Reservation",
            Namespace = "Booking",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            HasManualPersistenceId = true
        };

        model.NeedsTraitGeneration.Should().BeTrue();
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new OwnershipPropertyTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel(string ns, string name, bool isOwnedEntity = false)
    {
        return new EntityMetadataModel
        {
            TypeName = name,
            FullTypeName = $"{ns}.{name}",
            Namespace = ns,
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = isOwnedEntity
        };
    }
}

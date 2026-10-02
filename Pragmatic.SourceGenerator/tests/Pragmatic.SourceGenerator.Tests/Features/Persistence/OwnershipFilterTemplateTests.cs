using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class OwnershipFilterTemplateTests
{
    [Fact]
    public void OwnedEntity_GeneratesNestedOwnershipFilter()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("public sealed class OwnershipFilter");
        source.Should().Contain("IPermissionBasedFilter");
    }

    [Fact]
    public void OwnedEntity_FilterHasPriority200()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("Priority => 200");
    }

    [Fact]
    public void OwnedEntity_FilterHasOwnerIdExpression()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("entity => entity.OwnerId == currentUser.Id");
    }

    [Fact]
    public void OwnedEntity_WithBoundary_GeneratesBypassPermission()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "Reservation",
            FullTypeName = "Booking.Reservation",
            Namespace = "Booking",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            BoundaryName = "Booking"
        };

        var source = Render(model);

        source.Should().Contain("booking.reservation.view-all");
    }

    [Fact]
    public void OwnedEntity_WithoutBoundary_GeneratesBypassPermissionFromTypeName()
    {
        var model = BuildModel("Sales", "Order", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("order.view-all");
    }

    [Fact]
    public void OwnedEntity_PascalCase_ConvertedToKebab()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "RoomType",
            FullTypeName = "Catalog.RoomType",
            Namespace = "Catalog",
            IdType = "Guid",
            IsValid = true,
            IsOwnedEntity = true,
            BoundaryName = "Catalog"
        };

        var source = Render(model);

        source.Should().Contain("catalog.room-type.view-all");
    }

    [Fact]
    public void OwnedEntity_FilterInjectsICurrentUser()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("ICurrentUser currentUser");
    }

    [Fact]
    public void OwnedEntity_IsNestedInsidePartialClass()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var source = Render(model);

        source.Should().Contain("partial class Reservation");
    }

    [Fact]
    public void NonOwnedEntity_SkipsGeneration()
    {
        var model = BuildModel("Sales", "Order", isOwnedEntity: false);

        var template = new OwnershipFilterTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    [Fact]
    public void OwnedEntity_HintName_FollowsConvention()
    {
        var model = BuildModel("Booking", "Reservation", isOwnedEntity: true);

        var template = new OwnershipFilterTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Reservation");
        artifact.HintName.Should().Contain("OwnershipFilter");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new OwnershipFilterTemplate(model);
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

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class TemporalVirtualNavigationTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesActiveQueryMethod()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("QueryActiveStaffAssignments(");
        source.Should().Contain("this global::Catalog.Property parent");
        source.Should().Contain("IReadRepository<global::Booking.StaffAssignment>");
    }

    [Fact]
    public void RenderOutput_ActiveQueryFiltersOnParentAndTime()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("e.PropertyId == parent.Id");
        // The instant is an input. UtcNow is still the fallback, and still captured app-side
        // (parameterized) to avoid app↔DB clock skew — what changed is that a caller can name it.
        source.Should().Contain("global::System.TimeProvider? timeProvider = null");
        source.Should().Contain("var now = timeProvider?.GetUtcNow() ?? global::System.DateTimeOffset.UtcNow;");
        source.Should().Contain("e.ValidFrom <= now");
        source.Should().Contain("e.ValidTo == null || e.ValidTo > now");
    }

    [Fact]
    public void RenderOutput_GeneratesHistoryQueryMethod()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("QueryStaffAssignmentHistory(");
        source.Should().Contain("e.PropertyId == parent.Id");
    }

    [Fact]
    public void RenderOutput_GeneratesBatchMethod()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("LoadActiveStaffAssignmentsBatchAsync(");
        source.Should().Contain("IEnumerable<global::System.Guid> parentIds");
        source.Should().Contain("ILookup<global::System.Guid, global::Booking.StaffAssignment>");
    }

    [Fact]
    public void RenderOutput_GeneratesStaticExtensionClass()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("public static class PropertyStaffAssignmentNavigationExtensions");
    }

    [Fact]
    public void RenderOutput_UsesTemporalEntityNamespace()
    {
        var model = BuildModel();
        var source = Render(model);

        // Uses the temporal entity's namespace, not the parent's
        source.Should().Contain("namespace Booking;");
    }

    [Fact]
    public void RenderOutput_HintNameIncludesParentAndChild()
    {
        var model = BuildModel();
        var template = new TemporalVirtualNavigationTemplate(model);
        var artifact = template.RenderOutput();

        artifact.HintName.Should().Contain("Property");
        artifact.HintName.Should().Contain("StaffAssignment");
        artifact.HintName.Should().Contain("Navigation");
    }

    [Fact]
    public void Validate_NotTypedTemporal_DoesNotRender()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "StaffAssignment",
            FullTypeName = "Booking.StaffAssignment",
            Namespace = "Booking",
            IdType = "Guid",
            IsTemporalRelation = true,
            IsValid = true,
            // No TemporalParentTypeName → IsTypedTemporalRelation is false
        };

        var template = new TemporalVirtualNavigationTemplate(model);
        var artifact = template.RenderOutput();

        artifact.Text.Should().NotContain("class");
    }

    [Fact]
    public void Validate_MissingFkProperty_DoesNotRender()
    {
        var model = new EntityMetadataModel
        {
            TypeName = "StaffAssignment",
            FullTypeName = "Booking.StaffAssignment",
            Namespace = "Booking",
            IdType = "Guid",
            IsTemporalRelation = true,
            IsValid = true,
            TemporalParentTypeName = "Property",
            TemporalParentTypeFullName = "Catalog.Property",
            TemporalParentFkProperty = null // Missing FK
        };

        var template = new TemporalVirtualNavigationTemplate(model);
        var artifact = template.RenderOutput();

        artifact.Text.Should().NotContain("class");
    }

    private static string Render(EntityMetadataModel model)
    {
        var template = new TemporalVirtualNavigationTemplate(model);
        var artifact = template.RenderOutput();
        artifact.IsEmpty.Should().BeFalse();
        return artifact.Text;
    }

    private static EntityMetadataModel BuildModel()
    {
        return new EntityMetadataModel
        {
            TypeName = "StaffAssignment",
            FullTypeName = "Booking.StaffAssignment",
            Namespace = "Booking",
            IdType = "Guid",
            IsTemporalRelation = true,
            IsValid = true,
            TemporalParentTypeName = "Property",
            TemporalParentTypeFullName = "Catalog.Property",
            TemporalParentFkProperty = "PropertyId",
            Properties = ImmutableArray.Create(
                new PropertyMetadataModel
                {
                    Name = "PropertyId",
                    TypeName = "System.Guid",
                    IsNullable = false,
                    HasPrivateSetter = false
                })
        };
    }
}

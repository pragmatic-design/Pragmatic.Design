using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class CascadeHandlerTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesHandlerClass()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("LineItemUnitPriceCascadeHandler");
        source.Should().Contain("sealed class");
    }

    [Fact]
    public void RenderOutput_ImplementsIDomainEventHandler()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("IDomainEventHandler<global::Pragmatic.Events.EntityPropertyChanged<global::Sales.RoomType>>");
    }

    [Fact]
    public void RenderOutput_ChecksPropertyName()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("@event.PropertyName != \"Price\"");
    }

    [Fact]
    public void RenderOutput_UsesExecuteUpdateAsync()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("ExecuteUpdateAsync");
        // The boxed value is cast back to the target's concrete type so the provider can map the parameter.
        source.Should().Contain("SetProperty(e => e.UnitPrice, (decimal)@event.NewValue!)");
    }

    [Fact]
    public void RenderOutput_FiltersByForeignKey()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("e.RoomTypeId == entityId");
    }

    [Fact]
    public void RenderOutput_CastsEntityIdToFkType()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("var entityId = (global::System.Guid)@event.EntityId;");
    }

    [Fact]
    public void RenderOutput_WithCondition_AddsFilter()
    {
        var model = BuildModel(condition: "IsPending");
        var source = Render(model);

        // The boolean member is parenthesised so it composes correctly with the FK predicate.
        source.Should().Contain("e.RoomTypeId == entityId && (e.IsPending)");
    }

    [Fact]
    public void RenderOutput_WithoutCondition_NoExtraFilter()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().NotContain("IsPending");
    }

    [Fact]
    public void Validate_InvalidModel_ReturnsEmpty()
    {
        var model = new CascadeModel
        {
            Namespace = "Sales",
            SourceTypeName = "RoomType",
            SourceFullTypeName = "Sales.RoomType",
            SourceQualifiedTypeName = "global::Sales.RoomType",
            SourceProperty = "",
            TargetTypeName = "LineItem",
            TargetFullTypeName = "global::Sales.LineItem",
            TargetProperty = "",
            TargetPropertyTypeName = "decimal",
            ForeignKeyProperty = "RoomTypeId",
            ForeignKeyTypeName = "global::System.Guid"
        };

        var template = new CascadeHandlerTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    private static string Render(CascadeModel model)
    {
        var template = new CascadeHandlerTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static CascadeModel BuildModel(string? condition = null)
    {
        return new CascadeModel
        {
            Namespace = "Sales",
            SourceTypeName = "RoomType",
            SourceFullTypeName = "Sales.RoomType",
            SourceQualifiedTypeName = "global::Sales.RoomType",
            SourceProperty = "Price",
            TargetTypeName = "LineItem",
            TargetFullTypeName = "global::Sales.LineItem",
            TargetProperty = "UnitPrice",
            TargetPropertyTypeName = "decimal",
            ForeignKeyProperty = "RoomTypeId",
            ForeignKeyTypeName = "global::System.Guid",
            Condition = condition
        };
    }
}

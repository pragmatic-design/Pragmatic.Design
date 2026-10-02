using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class LoadingProfileTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesApplyIncludes()
    {
        var model = BuildModel("Sales", "OrderDetail", "Rooms");
        var source = Render(model);

        source.Should().Contain("ApplyIncludes");
        source.Should().Contain("Include(e => e.Rooms)");
    }

    [Fact]
    public void RenderOutput_NestedNavigation_UsesThenInclude()
    {
        var model = BuildModel("Sales", "OrderDetail", "Rooms.LineItems");
        var source = Render(model);

        source.Should().Contain("Include(e => e.Rooms)");
        source.Should().Contain("ThenInclude(e => e.LineItems)");
    }

    [Fact]
    public void RenderOutput_SplitQuery_AddsSplitQuery()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderDetail",
            FullTypeName = "global::Sales.OrderDetail",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order",
            SplitQuery = true,
            NavigationPaths = ImmutableArray.Create("Rooms", "LineItems")
        };

        var source = Render(model);
        source.Should().Contain("AsSplitQuery()");
    }

    [Fact]
    public void RenderOutput_NoNavigations_ReturnsEmpty()
    {
        var model = new LoadingProfileModel
        {
            Namespace = "Sales",
            TypeName = "OrderDetail",
            FullTypeName = "global::Sales.OrderDetail",
            EntityTypeName = "Order",
            EntityFullTypeName = "global::Sales.Order"
        };

        var template = new LoadingProfileTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    private static string Render(LoadingProfileModel model)
    {
        var template = new LoadingProfileTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static LoadingProfileModel BuildModel(string ns, string name, params string[] navigations)
    {
        return new LoadingProfileModel
        {
            Namespace = ns,
            TypeName = name,
            FullTypeName = $"global::{ns}.{name}",
            EntityTypeName = "Order",
            EntityFullTypeName = $"global::{ns}.Order",
            NavigationPaths = navigations.ToImmutableArray()
        };
    }
}

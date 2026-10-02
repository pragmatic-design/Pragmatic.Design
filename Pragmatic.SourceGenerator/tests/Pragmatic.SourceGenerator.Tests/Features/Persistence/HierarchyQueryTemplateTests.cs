using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Templates;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

public class HierarchyQueryTemplateTests
{
    [Fact]
    public void RenderOutput_GeneratesGetDescendants()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("GetDescendantsByParent");
        // The SQL dialect is chosen at runtime by HierarchyCteSql, not hard-coded.
        source.Should().Contain("HierarchyCteSql.Build(dbContext.Database");
        source.Should().Contain("Direction.Descendants");
        source.Should().Contain("\"ParentId\"");
        source.Should().NotContain("WITH Hierarchy AS"); // no hard-coded SQL-Server dialect anymore
    }

    [Fact]
    public void RenderOutput_GeneratesGetAncestors()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("GetAncestorsByParent");
        source.Should().Contain("Direction.Ancestors");
        source.Should().Contain("HierarchyCteSql.Build(dbContext.Database");
    }

    [Fact]
    public void RenderOutput_ResolvesTableAndColumnsFromEfModel_NotNaivePlural()
    {
        var model = BuildModel();
        var source = Render(model);

        // The table/column names come from the mapped model at runtime, honouring
        // [Table]/[Column]/irregular plurals — not a naive "{Type}s" literal.
        source.Should().Contain("dbContext.Model.FindEntityType(");
        source.Should().Contain(".GetTableName()");
        source.Should().Contain("FindProperty(\"ParentId\")?.GetColumnName()");
        source.Should().NotContain("\"Categorys\"");
    }

    [Fact]
    public void RenderOutput_GeneratesExtensionsClass()
    {
        var model = BuildModel();
        var source = Render(model);

        source.Should().Contain("CategoryHierarchyExtensions");
    }

    /// <summary>Two trees on one entity render two pairs, each named after its edge.</summary>
    [Fact]
    public void RenderOutput_TwoTrees_TwoPairsNamedAfterTheirEdges()
    {
        var model = BuildModel() with
        {
            Trees = new[]
            {
                new HierarchyTreeModel("ReportsTo", "ReportsToId"),
                new HierarchyTreeModel("MentoredBy", "MentoredById")
            }.ToEquatableArray()
        };
        var source = Render(model);

        source.Should().Contain("GetDescendantsByReportsTo");
        source.Should().Contain("GetAncestorsByMentoredBy");
        source.Should().Contain("FindProperty(\"MentoredById\")");
    }

    [Fact]
    public void Validate_NoTree_ReturnsEmpty()
    {
        var model = new HierarchyModel
        {
            Namespace = "Sales",
            TypeName = "Category",
            FullTypeName = "global::Sales.Category",
            IdType = "global::System.Guid"
        };

        var template = new HierarchyQueryTemplate(model);
        var artifact = template.RenderOutput();
        artifact.Text.Should().BeEmpty();
    }

    private static string Render(HierarchyModel model)
    {
        var template = new HierarchyQueryTemplate(model);
        var artifact = template.RenderOutput();
        return artifact.Text;
    }

    private static HierarchyModel BuildModel()
    {
        return new HierarchyModel
        {
            Namespace = "Sales",
            TypeName = "Category",
            FullTypeName = "global::Sales.Category",
            IdType = "global::System.Guid",
            Trees = new[] { new HierarchyTreeModel("Parent", "ParentId") }.ToEquatableArray()
        };
    }
}

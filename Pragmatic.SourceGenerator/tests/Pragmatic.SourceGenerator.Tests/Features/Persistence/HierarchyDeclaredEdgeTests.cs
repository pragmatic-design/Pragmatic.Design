using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     <c>[GenerateHierarchy]</c> leans on a declared self-referencing relation, and names its methods
///     after that relation's navigation.
/// </summary>
/// <remarks>
///     <para>
///         The attribute does not look for a member called <c>ParentId</c> with <c>GetMembers()</c>. That
///         finds only a hand-written property — the generated one lives in a file the transform cannot
///         see — so the framework would require exactly what the relation rules forbid.
///     </para>
///     <para>
///         With several relations to itself, an entity has several edges and the tree is one of them;
///         <c>Via</c> says which. An entity may carry several trees, and the navigation name in the
///         method is what keeps them from competing for one.
///     </para>
/// </remarks>
public class HierarchyDeclaredEdgeTests
{
    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string body)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Catalog;

            [Boundary]
            public partial class CatalogBoundary;

            {{body}}
            """, References);

    private static string Hierarchy(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(pair => pair.Key.Contains("HierarchyQuery"))
            .Value;

    [Fact]
    public void OneSelfRelation_IsTheTree_AndTheMethodsCarryItsName()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
            [GenerateHierarchy]
            public partial class Category : IEntity { }
            """);

        var source = Hierarchy(result);
        source.Should().Contain("GetDescendantsByParent(");
        source.Should().Contain("GetAncestorsByParent(");
        source.Should().Contain("FindProperty(\"ParentId\")",
            "the parent key comes from the relation, through the same naming every feature uses");
    }

    [Fact]
    public void TwoTrees_ProduceTwoPairsOfMethods()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Employee>.WithNavigation("ReportsTo", Required = false)]
            [Relation.ManyToOne<Employee>.WithNavigation("MentoredBy", Required = false)]
            [GenerateHierarchy(Via = "ReportsTo")]
            [GenerateHierarchy(Via = "MentoredBy")]
            public partial class Employee : IEntity { }
            """);

        var source = Hierarchy(result);
        source.Should().Contain("GetDescendantsByReportsTo(");
        source.Should().Contain("GetAncestorsByReportsTo(");
        source.Should().Contain("GetDescendantsByMentoredBy(");
        source.Should().Contain("GetAncestorsByMentoredBy(");
        source.Should().Contain("FindProperty(\"ReportsToId\")");
        source.Should().Contain("FindProperty(\"MentoredById\")");
    }

    [Fact]
    public void TwoSelfRelations_WithoutVia_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Employee>.WithNavigation("ReportsTo", Required = false)]
            [Relation.ManyToOne<Employee>.WithNavigation("MentoredBy", Required = false)]
            [GenerateHierarchy]
            public partial class Employee : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0713").Should().BeTrue(
            "the tree is one of two edges, and only a name can say which");
    }

    [Fact]
    public void ViaNamingNoSelfRelation_IsReported()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
            [GenerateHierarchy(Via = "Owner")]
            public partial class Category : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0714").Should().BeTrue();
    }

    [Fact]
    public void ARequiredEdge_CannotBeATree()
    {
        var result = Run("""
            [Entity]
            [Relation.ManyToOne<Category>.WithNavigation("Parent")]
            [GenerateHierarchy]
            public partial class Category : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0715").Should().BeTrue(
            "a root has no parent, so the edge has to be optional");
    }

    /// <summary>The control: a relation to another type is not a self-reference and is not a tree.</summary>
    [Fact]
    public void ARelationToAnotherType_IsNotAnEdge()
    {
        var result = Run("""
            [Entity]
            public partial class Section : IEntity { }

            [Entity]
            [Relation.ManyToOne<Section>.WithNavigation("Section", Required = false)]
            [GenerateHierarchy]
            public partial class Category : IEntity { }
            """);

        GeneratorTestHelper.HasDiagnostic(result, "PRAG0708").Should().BeTrue(
            "the hierarchy is a recursive CTE over one table, so the parent has to be the same entity");
    }
}

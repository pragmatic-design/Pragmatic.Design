using Microsoft.CodeAnalysis;
using Pragmatic.Actions.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A relation between entities on different engines keeps only the key (<c>PRAG0745</c>): no engine joins a table it
///     does not hold, so a navigation or a many-to-many there is refused at compile time rather than generated.
/// </summary>
/// <remarks>
///     Catalog is on EF Core and Sales on the Sql engine in every case; the cases differ by the relation and by
///     <c>[ReadAccess]</c> alone. The Sql engine's bridge is declared by the type the generator recognises it by, as in
///     <see cref="PersistenceEngineChoiceTests" />.
/// </remarks>
public class ARelationBetweenEnginesKeepsOnlyTheKeyTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<BoundaryAttribute>(),
        GeneratorTestHelper.FromType<Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()
    ];

    private static SourceGenRunResult Run(string salesEngine, string salesBoundaryAttributes, string orderRelation)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source(salesEngine, salesBoundaryAttributes, orderRelation), References);

    private static string Source(string salesEngine, string salesBoundaryAttributes, string orderRelation) => $$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Sales
            {
                [Boundary]
                [Owns<CatalogItem>]
                [PersistenceEngine(PersistenceEngineKind.EfCore)]
                public partial class CatalogBoundary;

                [Boundary]
                [Owns<Order>]
                [PersistenceEngine(PersistenceEngineKind.{{salesEngine}})]
                {{salesBoundaryAttributes}}
                public partial class SalesBoundary;

                [Entity]
                public partial class CatalogItem : IEntity { public string Name { get; private set; } = ""; }

                [Entity]
                {{orderRelation}}
                public partial class Order : IEntity { public string Reference { get; private set; } = ""; }
            }

            {{SqlBridge}}
            """;

    private static IReadOnlyList<Diagnostic> Prag0745(SourceGenRunResult result)
        => [.. GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG0745")];

    [Fact]
    public void ANavigationReadAcrossEngines_IsPrag0745_OnTheEntity()
    {
        const string relation = """[Relation.ManyToOne<CatalogItem>.WithNavigation("Item")]""";
        var result = Run("Sql", "[ReadAccess<CatalogItem>]", relation);

        var error = Prag0745(result).Should().ContainSingle(
            "[ReadAccess] would generate a navigation to a table the Sql engine does not hold").Which;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain("Contoso.Sales.Order").And.Contain("Item").And.Contain("Contoso.Sales.CatalogItem")
            .And.Contain("Sql").And.Contain("EfCore");
        error.Location.GetLineSpan().Path.Should().NotBeNullOrEmpty("the fix is on the entity that declares the relation");
        Source("Sql", "[ReadAccess<CatalogItem>]", relation).Split('\n')[error.Location.GetLineSpan().StartLinePosition.Line].Should().Contain("class Order");
    }

    /// <summary>The control: the same relation without <c>[ReadAccess]</c> generates only the key, which is what the rule keeps.</summary>
    [Fact]
    public void TheSameRelationWithoutReadAccess_KeepsTheKey_AndIsSilent()
    {
        var result = Run("Sql", "", """[Relation.ManyToOne<CatalogItem>.WithNavigation("Item")]""");

        Prag0745(result).Should().BeEmpty("only ItemId is generated, and a key needs no join");
    }

    /// <summary>The control for the engine: the same navigation between two boundaries on one engine is a join it can make.</summary>
    [Fact]
    public void ANavigationReadAcrossBoundariesOnOneEngine_IsSilent()
    {
        var result = Run("EfCore", "[ReadAccess<CatalogItem>]", """[Relation.ManyToOne<CatalogItem>.WithNavigation("Item")]""");

        Prag0745(result).Should().BeEmpty();
    }

    /// <summary>A many-to-many has no key to keep: its join rows would sit on one engine and point at the other's.</summary>
    [Fact]
    public void AManyToManyAcrossEngines_IsPrag0745_EvenWithoutReadAccess()
    {
        var result = Run("Sql", "", """[Relation.ManyToMany<CatalogItem>.WithNavigation("Items")]""");

        Prag0745(result).Should().ContainSingle().Which.GetMessage().Should().Contain("Items");
    }
}

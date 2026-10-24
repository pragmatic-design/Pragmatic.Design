using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The entity's shape is emitted for every engine; what only EF Core runs, for EF Core's entities only.
/// </summary>
/// <remarks>
///     <para>
///         The shape is what every engine reads off the partial class: the key, the stamps, the owner and scopes,
///         the relations, the change tracking, the filter classes callers name. Until the gate was split it was
///         emitted only when EF Core was referenced, so an application on another engine got an entity with none of
///         those members, and no generator sees another's output to declare them instead.
///     </para>
///     <para>
///         The Sql engine's bridge is not in the repository yet: a stub of the type the generator recognises it by
///         stands in for it, and in the first test it is the only engine referenced, EF Core's assemblies included.
///     </para>
/// </remarks>
public class TheShapeIsEmittedForEveryEngineTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    private const string Catalog = """
        [Boundary]
        public partial class CatalogBoundary;

        [Entity]
        [SoftDelete]
        [Auditable]
        public partial class Product
        {
            public string Name { get; private set; } = "";
        }
        """;

    [Fact]
    public void OnTheSqlEngineAlone_TheEntityGetsItsShape_AndItCompilesWithoutEfCore()
    {
        var result = Run(Catalog + SqlBridge, efCore: false);

        var hints = Hints(result);
        hints.Should().Contain(h => h.EndsWith("Product.Traits.g.cs"), "the key and the stamps are the shape");
        hints.Should().Contain(h => h.EndsWith("Product.Setters.g.cs"), "change tracking is the shape");
        hints.Should().Contain(h => h.Contains("Product.SoftDeleteFilter"), "callers name the filter class on every engine");
        hints.Should().NotContain(h => h.Contains("Repository"), "the repository is EF Core's");
        hints.Should().NotContain(h => h.Contains("EntityConfig") || h.Contains("DbContext"), "so is the mapping");

        GeneratorTestHelper.GetCompilationErrors(result).Should().BeEmpty(
            "the shape names nothing of EF Core, and EF Core is not referenced here");
    }

    /// <summary>The control: with no engine referenced, nothing is emitted, as before the split.</summary>
    [Fact]
    public void WithNoEngineReferenced_NothingIsEmitted()
    {
        var result = Run(Catalog, efCore: false);

        Hints(result).Should().NotContain(h => h.Contains("Product."), "no engine persists the entity");
    }

    [Fact]
    public void WithBothEngines_EachBoundaryGetsWhatItsEngineRuns()
    {
        var result = Run("""
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class CatalogBoundary;

            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.EfCore)]
            public partial class BillingBoundary;

            [Entity]
            [BelongsTo<CatalogBoundary>]
            public partial class Product
            {
                public string Name { get; private set; } = "";
            }

            [Entity]
            [BelongsTo<BillingBoundary>]
            public partial class Invoice
            {
                public string Number { get; private set; } = "";
            }
            """ + SqlBridge, efCore: true);

        var hints = Hints(result);
        hints.Should().Contain(h => h.EndsWith("Invoice.Repository.g.cs"), "Billing names EF Core");
        hints.Should().NotContain(h => h.EndsWith("Product.Repository.g.cs"), "Catalog names Sql");
        hints.Should().Contain(h => h.EndsWith("Product.Traits.g.cs"), "the shape is every engine's");
        hints.Should().Contain(h => h.EndsWith("Invoice.Traits.g.cs"));
        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG074").Should().BeEmpty(
            "each boundary names an engine the compilation references");
    }

    private static SourceGenRunResult Run(string body, bool efCore)
        => GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Catalog
            {
                {{body.Replace(SqlBridge, "")}}
            }

            {{(body.Contains(SqlBridge) ? SqlBridge : "")}}
            """,
            efCore ? [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()] : References);

    private static List<string> Hints(SourceGenRunResult result)
        => GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.ToList();

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        .. ByName("Pragmatic.Specification", "System.Linq.Queryable", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}

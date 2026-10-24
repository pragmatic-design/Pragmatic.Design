using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     The Sql engine serves the entities whose boundary names it with <c>[PersistenceEngine]</c>, read as the rest of the
///     generator reads it.
/// </summary>
/// <remarks>
///     The Sql feature came from a spike whose attribute lived in <c>Pragmatic.Persistence</c>; the repository's is in
///     <c>Pragmatic.Persistence.Entity</c>. Reading its own copy of the name, the feature saw no boundary name an engine, so
///     with two engines referenced it served no entity at all, while the shape and PRAG0743 read the boundary correctly.
///     The bridges and the runtime types the feature recognises are stubs: none is in the repository's generator tests. The
///     second engine is the documents one, not EF Core: with EF Core referenced the feature does not reach the choice at all
///     (it leaves the entities to the EF Core shape it cannot see, and reports PRAG3029 for a chain over them).
/// </remarks>
public class TheSqlEngineReadsTheBoundarysEngineTests
{
    private const string SqlStubs = """
        namespace Pragmatic.Persistence.Sql
        {
            public class SqlSession { }
        }

        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
            public class CompiledSpecification<T> { }
            public class SqlUnitOfWork { }
        }

        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
        }
        """;

    [Fact]
    public void WithTwoEngines_TheSqlEngineServesTheBoundaryThatNamesIt_AndNotTheOther()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>($$"""
            using Pragmatic.Actions.Attributes;
            using Pragmatic.Persistence.Entity;

            namespace Contoso.Catalog
            {
                [Boundary]
                [PersistenceEngine(PersistenceEngineKind.Sql)]
                public partial class CatalogBoundary;

                [Boundary]
                [PersistenceEngine(PersistenceEngineKind.Documents)]
                public partial class BillingBoundary;

                [Entity]
                [BelongsTo<CatalogBoundary>]
                public partial class Product : IEntity
                {
                    public string Name { get; private set; } = "";
                }

                [Entity]
                [BelongsTo<BillingBoundary>]
                public partial class Invoice : IEntity
                {
                    public string Number { get; private set; } = "";
                }
            }

            {{SqlStubs}}
            """,
            References);

        var hints = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.ToList();
        hints.Should().Contain("Contoso.Catalog.Product.SqlReadRepository.g.cs", "Catalog names the Sql engine");
        hints.Should().NotContain(h => h.Contains("Invoice.Sql"), "Billing names the documents engine");
    }

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

using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A chain on <c>SqlQuery&lt;T&gt;</c> over a Pragmatic entity binds against the shape Persistence emits for it, EF Core
///     referenced or not.
/// </summary>
/// <remarks>
///     The members a chain reads (<c>IsDeleted</c> here) are this generator's own output, which the compilation it is handed
///     does not have: the Sql feature adds Persistence's shape files to the compilation it binds in. It used to keep a copy of
///     the shape of its own, and with EF Core referenced it bound nothing at all and reported PRAG3029, though the shape is
///     emitted for every engine. The Sql bridge is a stub of the type the generator recognises it by.
/// </remarks>
public class TheSqlEngineBindsOnTheEntitysShapeTests
{
    private const string Source = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Persistence.Sql;

        namespace Contoso.Catalog
        {
            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.Sql)]
            public partial class CatalogBoundary;

            [Boundary]
            [PersistenceEngine(PersistenceEngineKind.EfCore)]
            public partial class BillingBoundary;

            [Entity]
            [BelongsTo<CatalogBoundary>]
            [SoftDelete]
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

            public static class Reads
            {
                public static System.Threading.Tasks.ValueTask<System.Collections.Generic.List<Product>> Deleted(SqlSession session)
                    => session.Query<Product>().Where(p => p.IsDeleted).ToListAsync();
            }
        }

        namespace Pragmatic.Persistence.Sql.Pragmatic
        {
            public static class PragmaticSqlFilters { }
        }
        """;

    [Fact]
    public void WithEfCoreReferencedToo_AChainOverTheShapeIsTranslated()
    {
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(Source,
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute>()]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3029").Should().BeEmpty("the shape is declared, by Persistence");
        var interceptors = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)
            .Single(s => s.Key == "_Infra.Sql.Interceptors.g.cs").Value;
        interceptors.Should().Contain("\"IsDeleted\"", "the chain's predicate reads the shape's column");
        interceptors.Should().Contain("InterceptsLocation", "the chain's call site is intercepted");
    }

    /// <summary>The control: with no engine's bridge referenced nobody declares the shape, and the chain is reported.</summary>
    [Fact]
    public void WithNoBridge_AChainOverTheMissingShapeIsReported()
    {
        var withoutBridge = Source.Replace("public static class PragmaticSqlFilters { }", "");
        var result = GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(withoutBridge, References);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3029").Should().NotBeEmpty("IsDeleted is declared by nobody");
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>(),
        .. ByName("Pragmatic.Specification", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}

using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     What the documents engine generates for Pragmatic's entities: only those whose boundary it serves, with their change detection, their
///     repository, their declared queries' shape and the specifications it compiles; and what it reports there (PRAG3101, PRAG3107, PRAG3108).
/// </summary>
/// <remarks>
///     The bridges are stubs of the types the generator recognises them by: none is referenced by the repository's generator tests. So these
///     tests read the hints, the diagnostics and the generated text, not a compiled output; the documents bridge suite
///     (<c>Pragmatic.Persistence.Documents.Pragmatic.Tests</c>) compiles and runs the same generation against the real bridge.
/// </remarks>
public class TheDocumentsEngineServesPragmaticsEntitiesTests
{
    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents.Pragmatic
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }

            // What the generated repository's chains call: without it they do not bind, and the generator reports them as not followed.
            public static class DocSpecifications
            {
                public static global::Pragmatic.Persistence.Documents.DocPredicate<T> ToPredicate<T>(global::Pragmatic.Specification.ISpecification<T> specification) => throw null!;
            }
        }
        """;

    private const string SqlBridge = """
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
        """;

    private const string TwoBoundaries = """
        using Pragmatic.Actions.Attributes;
        using Pragmatic.Persistence.Entity;
        using Pragmatic.Specification;

        namespace Two
        {
            [Boundary] [PersistenceEngine(PersistenceEngineKind.Sql)] public partial class CatalogBoundary;
            [Boundary] REVIEWS_MARKER public partial class ReviewsBoundary;

            [Entity] [BelongsTo<CatalogBoundary>] [SoftDelete]
            public partial class Product : IEntity { public string Name { get; set; } = ""; }

            [Entity] [BelongsTo<ReviewsBoundary>] [SoftDelete]
            public partial class Review : IEntity { public string Text { get; set; } = ""; }

            public static class Specs
            {
                public static Specification<Product> Named(string name) => Spec<Product>.Where(p => p.Name == name);
                public static Specification<Review> Saying(string text) => Spec<Review>.Where(r => r.Text == text);
            }
        }
        """;

    [Fact]
    public void WithTwoEngines_TheDocumentsEngineServesTheBoundaryThatNamesIt_AndCompilesOnlyItsSpecifications()
    {
        var result = Run(TwoBoundaries.Replace("REVIEWS_MARKER", "[PersistenceEngine(PersistenceEngineKind.Documents)]") + SqlBridge + DocumentsBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG074").Should().BeEmpty("each boundary names an engine that is referenced");
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain("Two.Review.Document.g.cs", "Reviews names the documents engine");
        sources.Keys.Should().Contain("Two.Review.DocumentTracking.g.cs");
        sources.Keys.Should().NotContain(k => k.StartsWith("Two.Product.Doc", StringComparison.Ordinal), "Catalog names the Sql engine");
        // Each Spec<T>.Where is intercepted by its engine only: never two interceptors on one call.
        var interceptors = sources["_Infra.Documents.Interceptors.g.cs"];
        interceptors.Should().Contain("DocCompiledSpecification<global::Two.Review>");
        interceptors.Should().NotContain("global::Two.Product");
    }

    [Fact]
    public void WithTheDocumentsEngineAlone_ItIsTheDefault_ForEveryBoundary()
    {
        var result = Run(TwoBoundaries.Replace("REVIEWS_MARKER", "").Replace("[PersistenceEngine(PersistenceEngineKind.Sql)] ", "") + DocumentsBridge);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG074").Should().BeEmpty("one engine referenced is the default");
        var hints = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.ToList();
        hints.Should().Contain("Two.Product.Document.g.cs");
        hints.Should().Contain("Two.Review.Document.g.cs");
        hints.Should().NotContain(h => h.Contains(".Sql", StringComparison.Ordinal), "the Sql engine is not referenced");
    }

    /// <summary>The control: without the documents bridge nothing is generated for Pragmatic's entities on the documents engine.</summary>
    [Fact]
    public void WithoutTheDocumentsBridge_NoEntityIsADocument()
    {
        var result = Run(TwoBoundaries.Replace("REVIEWS_MARKER", "").Replace("[PersistenceEngine(PersistenceEngineKind.Sql)] ", ""));

        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Keys.Should().NotContain(h => h.StartsWith("Two.", StringComparison.Ordinal) && h.Contains(".Doc", StringComparison.Ordinal));
    }

    private const string Rooms = """
        namespace Probe.Binding
        {
            [Pragmatic.Persistence.Entity.Entity] [Pragmatic.Persistence.Entity.SoftDelete]
            public partial class Room : Pragmatic.Persistence.Entity.IEntity { public string Code { get; set; } = ""; public int Beds { get; set; } }

            public sealed class RoomLine { public string Code { get; init; } = ""; public string Floor { get; init; } = ""; }

            [Pragmatic.Persistence.Query.Attributes.Query<Room, RoomLine>]
            public partial class RoomLinesQuery { public string? Code { get; init; } }

            [Pragmatic.Persistence.Query.Attributes.Query<Room>]
            public partial class RoomsQuery { public string? Code { get; init; } [Pragmatic.Persistence.Query.Attributes.Sort] public Pragmatic.Persistence.Query.SortDirection? BedsSort { get; init; } }
        }
        """;

    [Fact]
    public void AnEntityGetsItsChangeDetectionAndItsRepository()
    {
        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(Run(Rooms + DocumentsBridge));

        sources["Probe.Binding.Room.DocumentTracking.g.cs"].Should().Contain("global::Pragmatic.Persistence.Documents.IDocumentTracked<global::Probe.Binding.Room>");
        var repository = sources["Probe.Binding.Room.DocReadRepository.g.cs"];
        repository.Should().Contain("class DocRepository");
        repository.Should().Contain("global::Pragmatic.Persistence.Repository.IRepository<global::Probe.Binding.Room>");
    }

    [Fact]
    public void AQueryWhoseProjectionDoesNotMap_IsPrag3108_AndTheSortedOneGetsItsOrdering()
    {
        var result = Run(Rooms + DocumentsBridge);

        var refused = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3108").ToList();
        refused.Count.Should().Be(1);
        refused[0].GetMessage().Should().Contain("RoomLine.Floor", "the document has no Floor to project from");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Values
            .Should().Contain(s => s.Contains("partial class RoomsQuery", StringComparison.Ordinal) && s.Contains("ToDocOrdering", StringComparison.Ordinal));
    }

    [Fact]
    public void AnOrderedByBesideAnOrderBy_AndAToLowerOutsideAMatch_AreRefused()
    {
        var result = Run(Rooms + DocumentsBridge + """

            namespace Probe.Binding
            {
                public static class Reads
                {
                    public static System.Threading.Tasks.ValueTask<System.Collections.Generic.List<Room>> Twice(Pragmatic.Persistence.Documents.DocSession s, Pragmatic.Persistence.Documents.DocOrdering<Room> o)
                        => s.Query<Room>().OrderedBy(o).OrderBy(r => r.Beds).ToListAsync();

                    public static System.Threading.Tasks.ValueTask<long> Lowered(Pragmatic.Persistence.Documents.DocSession s)
                        => s.Query<Room>().CountAsync(r => r.Code.ToLower().Length > 2);
                }
            }
            """);

        var refusals = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3101").Select(d => d.GetMessage()).ToList();
        refusals.Should().Contain(m => m.Contains("one ordering per chain", StringComparison.Ordinal));
        refusals.Should().Contain(m => m.Contains("one side of a comparison is a field of the document", StringComparison.Ordinal), "a ToLower read for its Length");
        refusals.Count.Should().Be(2);
    }

    [Fact]
    public void AFieldsEqualsWithOneValue_IsAComparison()
    {
        var result = Run(Rooms + DocumentsBridge + """

            namespace Probe.Binding
            {
                public static class Reads
                {
                    public static System.Threading.Tasks.ValueTask<long> ByCode(Pragmatic.Persistence.Documents.DocSession s, string code)
                        => s.Query<Room>().CountAsync((r, c) => r.Code.Equals(c), code);
                }
            }
            """);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3101").Should().BeEmpty("Equals with one value is translated as ==");
        GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result)["_Infra.Documents.Interceptors.g.cs"].Should().Contain("\"$eq\"");
    }

    [Fact]
    public void AnEntityWithMoreFieldsThanAMaskHolds_IsPrag3107()
    {
        var properties = string.Concat(Enumerable.Range(0, 65).Select(i => $" public int F{i} {{ get; set; }}"));
        var result = Run($"namespace Probe.Binding {{ [Pragmatic.Persistence.Entity.Entity] public partial class Wide {{{properties} }} }}" + DocumentsBridge);

        var wide = GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3107").ToList();
        wide.Count.Should().Be(1);
        wide[0].GetMessage().Should().Contain("'Wide' has 65 document fields");
    }

    private static SourceGenRunResult Run(string source)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(source, "Pragmatic.Persistence.Documents.Generated", References);

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.Mongo.MongoClient>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}

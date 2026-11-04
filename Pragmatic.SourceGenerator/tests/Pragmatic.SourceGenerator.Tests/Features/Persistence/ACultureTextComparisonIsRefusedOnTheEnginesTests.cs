using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A text match with a <c>StringComparison</c> in a specification (#495): ordinal and ignoring case are translated by both engines (run in
///     their suites); a culture's rules are not something a database applies, so both refuse it, naming the comparison, rather than match by
///     other rules than the code says.
/// </summary>
/// <remarks>The bridges are declared by the types the generator recognises them by, as in <see cref="AComplexFilterIsComposedOnSqlTests" />.</remarks>
public class ACultureTextComparisonIsRefusedOnTheEnginesTests
{
    private const string SqlBridge = """
        namespace Pragmatic.Persistence.Sql
        {
            public static class PragmaticSqlFilters { }
            public class CompiledSpecification<T> { }
            public class SqlUnitOfWork { }
        }
        """;

    private const string DocumentsBridge = """
        namespace Pragmatic.Persistence.Documents
        {
            public static class PragmaticDocFilters { }
            public sealed class DocCompiledSpecification<T> { }
            public sealed class DocUnitOfWork { }
        }
        """;

    private const string Hotels = """
        using Pragmatic.Specification;

        namespace Probe.Text
        {
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Hotel : Pragmatic.Persistence.Entity.IEntity { public string Name { get; set; } = ""; }

            public static class HotelSpecifications
            {
                public static Specification<Hotel> NamedLike(string text) => Spec<Hotel>.Where(h => h.Name.Contains(text, System.StringComparison.OrdinalIgnoreCase));

                public static Specification<Hotel> NamedByCulture(string text) => Spec<Hotel>.Where(h => h.Name.StartsWith(text, System.StringComparison.CurrentCultureIgnoreCase));
            }
        }
        """;

    [Fact]
    public void OnTheSqlEngine_ACultureComparisonIsRefusedByName_AndIgnoringCaseIsNot()
    {
        var refused = Diagnostics(SqlBridge, "Pragmatic.Persistence.Sql.Generated", GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>(), "PRAG3031");

        refused.Should().ContainSingle("one specification is refused: the one ignoring case is translated");
        refused[0].Should().Contain("CurrentCultureIgnoreCase", "the culture's comparison is named");
    }

    [Fact]
    public void OnTheDocumentsEngine_ACultureComparisonIsRefusedByName_AndIgnoringCaseIsNot()
    {
        var refused = Diagnostics(DocumentsBridge, "Pragmatic.Persistence.Documents.Generated", GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>(), "PRAG3101");

        refused.Should().ContainSingle("one specification is refused: the one ignoring case is translated");
        refused[0].Should().Contain("CurrentCultureIgnoreCase", "the culture's comparison is named");
    }

    private static List<string> Diagnostics(string bridge, string interceptors, MetadataReference engine, string id)
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Hotels + bridge, interceptors, [.. References, engine]);
        // The comparisons' refusals only: with the bridges declared by name, the read repository's own chains have refusals of their own here.
        return [.. GeneratorTestHelper.GetGeneratorDiagnostics(result, id).Select(d => d.GetMessage()).Where(m => m.Contains("StringComparison", StringComparison.Ordinal))];
    }

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}

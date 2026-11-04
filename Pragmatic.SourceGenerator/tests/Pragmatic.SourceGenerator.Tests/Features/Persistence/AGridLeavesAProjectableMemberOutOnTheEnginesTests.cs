using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A grid adapter over an entity with a <c>[Projectable]</c> member, on the engines (#516). Neither engine translates a predicate on the
///     member's body, so its grid leaves the member out of its fields and reports it as a part of the declaration it does not compile, as it
///     reports every other one (PRAG3034, PRAG3109), instead of writing a predicate it then refuses inside a generated file. The entity's own
///     columns stay fields of the grid.
/// </summary>
/// <remarks>
///     The bridges are declared by the types the generator recognises them by, as in <see cref="ACultureTextComparisonIsRefusedOnTheEnginesTests" />.
///     The member's predicates are refused only once a grid writes them, so the refusal is looked for by the member's name.
/// </remarks>
public class AGridLeavesAProjectableMemberOutOnTheEnginesTests
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
        using Pragmatic.Persistence.Query.Attributes;

        namespace Probe.Grid
        {
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Hotel : Pragmatic.Persistence.Entity.IEntity
            {
                public string Name { get; set; } = "";

                public int Stars { get; set; }

                [Projectable]
                public int Score => Stars * 10;
            }

            [GridAdapter<Hotel>(Framework = GridFramework.DevExpress)]
            public partial class HotelGrid;
        }
        """;

    [Fact]
    public void OnTheSqlEngine_TheMemberIsReportedAndLeftOut_AndTheColumnsStay()
    {
        var result = Run(SqlBridge, "Pragmatic.Persistence.Sql.Generated", GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>());

        Messages(result, "PRAG3032").Concat(Messages(result, "PRAG3031")).Should().NotContain(m => m.Contains("Score", StringComparison.Ordinal),
            "no predicate on the member is written to be refused");
        Messages(result, "PRAG3034").Should().ContainSingle(m => m.Contains("HotelGrid", StringComparison.Ordinal) && m.Contains("Score", StringComparison.Ordinal),
            "the grid says what it leaves out");
        Grid(result, "HotelGrid.SqlGrid").Should().Contain("\"stars\"", "the entity's columns stay fields of the grid");
    }

    [Fact]
    public void OnTheDocumentsEngine_TheMemberIsReportedAndLeftOut_AndTheColumnsStay()
    {
        var result = Run(DocumentsBridge, "Pragmatic.Persistence.Documents.Generated", GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>());

        Messages(result, "PRAG3101").Should().NotContain(m => m.Contains("Score", StringComparison.Ordinal), "no predicate on the member is written to be refused");
        Messages(result, "PRAG3109").Should().ContainSingle(m => m.Contains("HotelGrid", StringComparison.Ordinal) && m.Contains("Score", StringComparison.Ordinal),
            "the grid says what it leaves out");
        Grid(result, "HotelGrid.DocGrid").Should().Contain("\"stars\"", "the entity's columns stay fields of the grid");
    }

    private static SourceGenRunResult Run(string bridge, string interceptors, MetadataReference engine)
        => GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Hotels + bridge, interceptors, [.. References, engine]);

    private static List<string> Messages(SourceGenRunResult result, string id)
        => [.. GeneratorTestHelper.GetGeneratorDiagnostics(result, id).Select(d => d.GetMessage())];

    private static string Grid(SourceGenRunResult result, string hint)
        => GeneratorTestHelper.GetGeneratedSource(result, hint) ?? throw new InvalidOperationException($"No {hint} was generated.");

    private static readonly MetadataReference[] References =
    [
        GeneratorTestHelper.FromType<EntityAttribute>(),
        GeneratorTestHelper.FromType<IEntity>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Query.Attributes.GridAdapterAttribute<object>>(),
        GeneratorTestHelper.FromType<global::Pragmatic.Actions.Attributes.BoundaryAttribute>(),
        .. ByName("Pragmatic.Specification", "Pragmatic.Abstractions", "Pragmatic.Persistence.Expressions", "System.Linq.Queryable", "System.Linq.Expressions", "System.Text.Json", "System.ComponentModel.Annotations")
    ];

    private static IEnumerable<MetadataReference> ByName(params string[] names)
        => names.Select(n => GeneratorTestHelper.TryGetAssemblyReference(n)
            ?? throw new InvalidOperationException($"'{n}' does not resolve in the test process."));
}

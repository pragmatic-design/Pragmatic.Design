using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.SourceGenerator.Tests.Features.Persistence;

/// <summary>
///     A declared query's <c>[ComplexFilter]</c> on the Sql engine (#477): the filter object's members are written into the query's
///     <c>ToSpecification()</c> as <c>[FilterDto]</c> composes them, a <c>[FilterGroup(FilterLogic.Or)]</c> joining its members with OR. Before, the
///     attribute refused the query (PRAG3033), as it refused the Showcase's <c>SearchPropertiesQuery</c>. The documents engine still refuses it.
/// </summary>
/// <remarks>The bridges are declared by the types the generator recognises them by; the composition itself runs in <c>Pragmatic.Persistence.Sql.Tests</c>.</remarks>
public class AComplexFilterIsComposedOnSqlTests
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
        using Pragmatic.Persistence.Query;
        using Pragmatic.Persistence.Query.Attributes;

        namespace Probe.Complex
        {
            [Pragmatic.Persistence.Entity.Entity]
            public partial class Hotel : Pragmatic.Persistence.Entity.IEntity
            {
                public string Name { get; set; } = "";
                public string City { get; set; } = "";
                public string Country { get; set; } = "";
                public int Stars { get; set; }
            }

            [FilterDto<Hotel>]
            public partial class PlaceFilter
            {
                [Filter(Operator = FilterOperator.Contains)] public string? City { get; set; }
                [Filter(Operator = FilterOperator.Contains)] public string? Country { get; set; }
            }

            [FilterDto<Hotel>]
            public partial class HotelFilter
            {
                [FilterGroup(FilterLogic.Or)] public PlaceFilter? Place { get; set; }
                [Filter(Operator = FilterOperator.LessOrEqual, MapTo = "Stars")] public int? MaxStars { get; set; }
            }

            [Query<Hotel>]
            public partial class SearchHotelsQuery
            {
                [Filter] public string? Name { get; init; }
                [ComplexFilter] public HotelFilter? Where { get; init; }
            }
        }
        """;

    [Fact]
    public void OnTheSqlEngine_TheFilterObjectIsComposedIntoTheSpecification()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Hotels + SqlBridge, "Pragmatic.Persistence.Sql.Generated",
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Sql.SqlSession>()]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3033").Should().BeEmpty("the filter object is compiled, not refused");        var shape = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result).Single(s => s.Key.Contains("SearchHotelsQuery.SqlQueryShape")).Value;
        shape.Should().Contain("if (this.Where is not null)", "an unset filter object filters nothing");
        shape.Should().Contain("e => e.Stars <= this.Where!.MaxStars.Value", "the member maps to the entity's field it names");
        shape.Should().Contain("e => e.City.Contains(this.Where!.Place!.City!)");
        shape.Should().Contain("__group1 = __group1 is null ? __leaf : (__group1 | __leaf)", "the members of an Or group are joined with OR");
        shape.Should().Contain("__group0 = __group0 is null ? __leaf : (__group0 & __leaf)", "the filter object's own members with AND");
        shape.Should().Contain("if (__group0 is not null) spec = spec & __group0;", "and the filter object with the query's other filters");
    }

    /// <summary>The control: the documents engine reads the same declaration and still refuses it, by name.</summary>
    [Fact]
    public void OnTheDocumentsEngine_TheQueryIsStillRefused()
    {
        var result = GeneratorTestHelper.RunGeneratorWithInterceptors<PragmaticSourceGenerator>(Hotels + DocumentsBridge, "Pragmatic.Persistence.Documents.Generated",
            [.. References, GeneratorTestHelper.FromType<global::Pragmatic.Persistence.Documents.DocSession>()]);

        GeneratorTestHelper.GetGeneratorDiagnostics(result, "PRAG3108").Should().Contain(d => d.GetMessage().Contains("[ComplexFilter] is not compiled on the documents engine", StringComparison.Ordinal));
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

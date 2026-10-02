using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Query;
using Pragmatic.Persistence.Query.Attributes;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Tests for [ComplexFilter] support:
///     - Query Apply() generates ApplyFilter call for [ComplexFilter] properties
///     - FilterDto TypeConverter is emitted on every [FilterDto] class
///     - [ComplexFilter] properties are NOT treated as flat filters
/// </summary>
public class ComplexFilterGeneratorTests
{
    [Fact]
    public void ComplexFilter_WithNestedGroup_GeneratesApplyFilterCall()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Property : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string City { get; set; } = "";
                public string Country { get; set; } = "";
            }

            [FilterDto<Property>]
            public partial class CityGroupFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? City { get; set; }
                [Filter(Operator = FilterOperator.Contains)]
                public string? Country { get; set; }
            }

            [FilterDto<Property>]
            public partial class PropertyLocationFilter
            {
                [FilterGroup(FilterLogic.Or)]
                public CityGroupFilter? CityGroup { get; set; }
            }

            [Query<Property>]
            public partial class SearchQuery
            {
                [Filter]
                public string? Name { get; set; }

                [ComplexFilter]
                public PropertyLocationFilter? Location { get; set; }
            }
            """;

        var result = RunGenerator(source);

        result.Diagnostics.Should().BeEmpty();

        var querySource = GeneratorTestHelper.GetGeneratedSource(result, "Query");
        querySource.Should().NotBeNull();
        querySource.Should().Contain("PropertyLocationFilterExtensions.ApplyFilter(query, this.Location)");
        querySource.Should().Contain("Apply complex filter groups");
    }

    [Fact]
    public void ComplexFilter_PropertyIsNotTreatedAsFlatFilter()
    {
        // [ComplexFilter] properties must NOT appear in the flat filter conditions
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Order>]
            public partial class NameFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; set; }
            }

            [Query<Order>]
            public partial class SearchOrdersQuery
            {
                [ComplexFilter]
                public NameFilter? Filter { get; set; }
            }
            """;

        var result = RunGenerator(source);

        result.Diagnostics.Should().BeEmpty();

        var querySource = GeneratorTestHelper.GetGeneratedSource(result, "Query");
        querySource.Should().NotBeNull();
        // Flat filter block for "Filter" property must NOT be generated
        querySource.Should().NotContain("this.Filter ==");
        querySource.Should().NotContain("this.Filter.Contains");
        querySource.Should().NotContain("this.Filter is not null");
        // The ApplyFilter delegate call must be generated
        querySource.Should().Contain("NameFilterExtensions.ApplyFilter(query, this.Filter)");
    }

    [Fact]
    public void FilterDto_TypeConverter_IsGenerated()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Order>]
            public partial class OrderFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        result.Diagnostics.Should().BeEmpty();

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var converterSource = sources.Values.FirstOrDefault(s => s.Contains("TypeConverter") && s.Contains("OrderFilter"));
        converterSource.Should().NotBeNull();
        converterSource.Should().Contain("JsonQueryConverter<global::TestApp.OrderFilter>");
        converterSource.Should().Contain("public partial class OrderFilter");
    }

    [Fact]
    public void FilterDto_TypeConverter_HintName_ContainsNamespaceAndTypeName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace MyApp.Search;

            public class Customer : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Customer>]
            public partial class CustomerFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        result.Diagnostics.Should().BeEmpty();

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("CustomerFilter") && k.Contains("TypeConverter"));
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(source, GetReferences());
    }

    private static MetadataReference[] GetReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterDtoAttribute<object>>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
            GeneratorTestHelper.FromType<ComplexFilterAttribute>(),
        ];
    }
}

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
///     Integration tests for the FilterDto source generator pipeline.
///     Runs the actual PragmaticSourceGenerator on source code with [FilterDto] attributes
///     and verifies the generated output (Transform → Template → Source).
/// </summary>
public class FilterDtoGeneratorTests
{
    [Fact]
    public void SimpleFilterDto_GeneratesExtensionClass()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                public decimal Total { get; set; }
            }

            [FilterDto<Order>]
            public partial class OrderFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain("OrderFilterExtensions");
        generated.Should().Contain("ApplyFilter");
        generated.Should().Contain("ToSpecification");
    }

    [Fact]
    public void FilterDto_WithContainsOperator_GeneratesContains()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Product : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Product>]
            public partial class ProductFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain(".Contains(");
    }

    [Fact]
    public void FilterDto_WithMapTo_GeneratesNestedPath()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Customer
            {
                public string Name { get; set; } = "";
            }

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public Customer? Customer { get; set; }
            }

            [FilterDto<Order>]
            public partial class OrderFilter
            {
                [Filter(MapTo = "Customer.Name", Operator = FilterOperator.Contains)]
                public string? CustomerName { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain("Customer.Name");
    }

    [Fact]
    public void FilterDto_WithIgnoreCase_GeneratesToLower()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Product : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Product>]
            public partial class ProductFilter
            {
                [Filter(Operator = FilterOperator.Contains, IgnoreCase = true)]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain(".ToLower()");
    }

    [Fact]
    public void FilterDto_WithMultipleOperators_GeneratesAll()
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
                public decimal Total { get; set; }
            }

            [FilterDto<Order>]
            public partial class OrderFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; set; }

                [Filter(MapTo = "Total", Operator = FilterOperator.GreaterOrEqual)]
                public decimal? MinTotal { get; set; }

                [Filter(MapTo = "Total", Operator = FilterOperator.LessOrEqual)]
                public decimal? MaxTotal { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain(".Contains(");
        generated.Should().Contain(">= filter.MinTotal");
        generated.Should().Contain("<= filter.MaxTotal");
    }

    [Fact]
    public void FilterDto_WithFilterGroup_GeneratesGroupCall()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public System.DateTime CreatedAt { get; set; }
            }

            [FilterDto<Order>]
            public partial class DateRangeFilter
            {
                [Filter(MapTo = "CreatedAt", Operator = FilterOperator.GreaterOrEqual)]
                public System.DateTime? From { get; set; }

                [Filter(MapTo = "CreatedAt", Operator = FilterOperator.LessOrEqual)]
                public System.DateTime? To { get; set; }
            }

            [FilterDto<Order>]
            public partial class OrderFilter
            {
                [FilterGroup(FilterLogic.And)]
                public DateRangeFilter? DateRange { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var orderFilterSource = sources.Values.FirstOrDefault(s => s.Contains("OrderFilterExtensions"));
        orderFilterSource.Should().NotBeNull();
        orderFilterSource.Should().Contain("DateRangeFilterExtensions.ToSpecification");
    }

    [Fact]
    public void FilterDto_WithOrFilterGroup_GeneratesUseOrLogicTrue()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Product : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Product>]
            public partial class KeywordFilter
            {
                [Filter(Operator = FilterOperator.Contains)]
                public string? Name { get; set; }
            }

            [FilterDto<Product>]
            public partial class ProductFilter
            {
                [FilterGroup(FilterLogic.Or)]
                public KeywordFilter? Keywords { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        var productFilterSource = sources.Values.FirstOrDefault(s => s.Contains("ProductFilterExtensions"));
        productFilterSource.Should().NotBeNull();
        productFilterSource.Should().Contain("useOrLogic: true");
    }

    [Fact]
    public void FilterDto_StringDefaultOperator_IsContains()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Product : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Product>]
            public partial class ProductFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        // String properties default to Contains operator
        generated.Should().Contain(".Contains(");
    }

    [Fact]
    public void FilterDto_NonPartialClass_GeneratesNothing()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
            }

            [FilterDto<Order>]
            public class OrderFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().BeNull();
    }

    [Fact]
    public void FilterDto_GeneratesCorrectHintName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace MyApp.Sales;

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

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k => k.Contains("OrderFilter") && k.Contains("FilterDto"));
    }

    [Fact]
    public void FilterDto_InternalClass_GeneratesInternalExtensions()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Persistence.Query.Attributes;

            namespace TestApp;

            public class Order : IEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [FilterDto<Order>]
            internal partial class OrderFilter
            {
                [Filter]
                public string? Name { get; set; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "FilterDto");
        generated.Should().NotBeNull();
        generated.Should().Contain("internal static partial class OrderFilterExtensions");
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GetPersistenceReferences());
    }

    private static MetadataReference[] GetPersistenceReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<FilterDtoAttribute<object>>(),
            GeneratorTestHelper.FromType<FilterOperator>(),
        ];
    }
}

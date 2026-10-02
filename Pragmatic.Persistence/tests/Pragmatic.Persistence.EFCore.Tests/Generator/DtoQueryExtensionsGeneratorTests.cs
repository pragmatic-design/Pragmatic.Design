using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Tests for the DTO query extensions source generation pipeline.
///     Verifies that [MapFrom&lt;TEntity&gt;] + [GenerateProjection] DTOs trigger
///     GetAs{Dto}Async, ListAs{Dto}Async, and As{Dto} extension methods on IQueryable.
/// </summary>
public class DtoQueryExtensionsGeneratorTests
{
    [Fact]
    public void DtoWithProjection_GeneratesQueryExtensions()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                public System.Guid CustomerId { get; set; }
                public Customer Customer { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderDetailDto
            {
                public string Name { get; init; } = "";
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull("DtoQuery extensions should be generated");
        generated.Should().Contain("OrderDtoQueryExtensions");
        generated.Should().Contain("AsOrderDetailDto");
        generated.Should().Contain("GetAsOrderDetailDtoAsync");
        generated.Should().Contain("ListAsOrderDetailDtoAsync");
    }

    [Fact]
    public void AsMethod_ProjectsWithoutAnInclude()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                public System.Guid CustomerId { get; set; }
                public Customer Customer { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderDetailDto
            {
                public string Name { get; init; } = "";
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();

        generated.Should().Contain(".Projection");
        generated.Should().NotContain("WithIncludesForOrderDetailDto",
            "a projection is already a JOIN — measured against SQLite, EF drops an include whose "
            + "entity does not survive into the result, so composing the two was a call that did nothing");
    }

    [Fact]
    public void GetAsMethod_UsesWhereAndFirstOrDefault()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class SimpleBoundary;

            [Entity]
            [BelongsTo<SimpleBoundary>]
            public partial class Product
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<Product>]
            [GenerateProjection]
            public partial record ProductDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();
        generated.Should().Contain("e.PersistenceId == id");
        generated.Should().Contain("FirstOrDefaultAsync");
    }

    [Fact]
    public void ListAsMethod_UsesToListAsync()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class SimpleBoundary;

            [Entity]
            [BelongsTo<SimpleBoundary>]
            public partial class Product
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<Product>]
            [GenerateProjection]
            public partial record ProductDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();
        generated.Should().Contain("ToListAsync");
    }

    [Fact]
    public void DtoWithoutProjection_GeneratesNoQueryExtensions()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class SimpleBoundary;

            [Entity]
            [BelongsTo<SimpleBoundary>]
            public partial class Product
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            // No [GenerateProjection] → no query extensions
            [MapFrom<Product>]
            public partial record ProductDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().BeNull("no query extensions for DTOs without [GenerateProjection]");
    }

    [Fact]
    public void MultipleDtos_GeneratesMethodsForEach()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                public System.Guid CustomerId { get; set; }
                public Customer Customer { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderDetailDto
            {
                public string Name { get; init; } = "";
                public Customer Customer { get; init; }
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderSummaryDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();
        generated.Should().Contain("AsOrderDetailDto");
        generated.Should().Contain("AsOrderSummaryDto");
        generated.Should().Contain("GetAsOrderDetailDtoAsync");
        generated.Should().Contain("GetAsOrderSummaryDtoAsync");
    }

    [Fact]
    public void DtoWithoutNavs_OmitsIncludeCall()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
                public System.Guid CustomerId { get; set; }
                public Customer Customer { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            // Flat DTO — no navigations, just scalar properties
            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderFlatDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();

        // Flat DTO should NOT have WithIncludes call
        generated.Should().NotContain("WithIncludesForOrderFlatDto");

        // But should still have projection
        generated.Should().Contain("OrderFlatDto.Projection");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace MyApp.Sales;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<Order>]
            [GenerateProjection]
            public partial record OrderDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k =>
            k.Contains("Order") && k.Contains("Projections"));
    }

    [Fact]
    public void GeneratesCorrectHeader()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class TestBoundary;

            [Entity]
            [BelongsTo<TestBoundary>]
            public partial class Product
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<Product>]
            [GenerateProjection]
            public partial record ProductDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Projections");
        generated.Should().NotBeNull();
        generated.Should().Contain("// Pragmatic.SourceGenerator/Persistence");
        generated.Should().Contain("DTO query extensions for Product");
    }

    private static SourceGenRunResult RunGenerator(string source)
    {
        return GeneratorTestHelper.RunGenerator<PragmaticSourceGenerator>(
            source,
            GetReferences());
    }

    private static MetadataReference[] GetReferences()
    {
        return
        [
            GeneratorTestHelper.FromType<IEntity>(),
            GeneratorTestHelper.FromType<PragmaticDbContextAttribute>(),
            GeneratorTestHelper.FromType<EntityAttribute>(),
            GeneratorTestHelper.FromType<BelongsToAttribute<object>>(),
            GeneratorTestHelper.FromType<MapFromAttribute<object>>(),
            GeneratorTestHelper.FromType<GenerateProjectionAttribute>(),
        ];
    }
}

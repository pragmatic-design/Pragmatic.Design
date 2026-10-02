using Pragmatic.Testing.Assertions;
using Microsoft.CodeAnalysis;
using Pragmatic.Mapping.Attributes;
using Pragmatic.Persistence.Entity;
using Pragmatic.SourceGen.Testing;
using Pragmatic.SourceGenerator;
using Xunit;

namespace Pragmatic.Persistence.EFCore.Tests.Generator;

/// <summary>
///     Tests for the DTO-aware Include Helper source generation pipeline.
///     Verifies that [MapFrom&lt;TEntity&gt;] DTOs trigger per-DTO Include extension methods
///     and a WithAllRelations fallback on IQueryable&lt;TEntity&gt;.
/// </summary>
/// <remarks>
///     The fixtures declare their relations with <c>[Relation.*]</c>: a navigation or a key typed as a
///     property is <c>PRAG0619</c> and is not read as a relation. ⚠️ This suite sits in the container
///     tier, so a rule change that breaks these fixtures surfaces here, behind the suites that run
///     before it.
/// </remarks>
public class IncludeHelperGeneratorTests
{
    [Fact]
    public void EntityWithNavs_AndMappedDto_GeneratesIncludeExtensions()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class CatalogBoundary;

            [Entity]
            [BelongsTo<CatalogBoundary>]
            [Relation.ManyToOne<RoomType>.WithNavigation("RoomType")]
            [Relation.OneToMany<Amenity>.WithNavigation("Amenities")]
            public partial class Property
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<CatalogBoundary>]
            public partial class RoomType
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<CatalogBoundary>]
            public partial class Amenity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<Property>]
            public partial record PropertyDetailDto
            {
                public string Name { get; init; } = "";
                public RoomType RoomType { get; init; }
                public ICollection<Amenity> Amenities { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull("Include extensions should be generated");
        generated.Should().Contain("PropertyIncludeExtensions");
        generated.Should().Contain("WithIncludesForPropertyDetailDto");
        generated.Should().Contain("WithAllRelations");
    }

    [Fact]
    public void PerDtoInclude_OnlyIncludesMatchedNavigations()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.OneToMany<LineItem>.WithNavigation("Lines")]
            [Relation.OneToMany<Payment>.WithNavigation("Payments")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string OrderNumber { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Payment
            {
                public System.Guid PersistenceId { get; set; }
            }

            // Only references Customer — should NOT include Lines or Payments
            [MapFrom<Order>]
            public partial record OrderSummaryDto
            {
                public string OrderNumber { get; init; } = "";
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();

        // Per-DTO method should only include Customer
        generated.Should().Contain("WithIncludesForOrderSummaryDto");
        generated.Should().Contain(".Include(e => e.Customer)");

        // WithAllRelations should include all same-boundary navs
        generated.Should().Contain("WithAllRelations");
        generated.Should().Contain("e.Lines");
        generated.Should().Contain("e.Payments");
    }

    [Fact]
    public void MultipleDtos_GeneratesPerDtoMethods()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.OneToMany<LineItem>.WithNavigation("Lines")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string OrderNumber { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            public partial record OrderDetailDto
            {
                public Customer Customer { get; init; }
                public ICollection<LineItem> Lines { get; init; }
            }

            [MapFrom<Order>]
            public partial record OrderSummaryDto
            {
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();
        generated.Should().Contain("WithIncludesForOrderDetailDto");
        generated.Should().Contain("WithIncludesForOrderSummaryDto");
    }

    /// <summary>
    ///     Every mapped DTO gets its method, and what it loads is the DTO's own list.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Membership is not decided by matching DTO property names against entity navigation names:
    ///         that way a DTO that flattens gets nothing — <c>RoomTypeSummaryDto</c> has
    ///         <c>PropertyName</c>, the navigation is <c>Property</c> — and the DTO that most needs its
    ///         navigation loaded is the one excluded.
    ///     </para>
    ///     <para>
    ///         The method loops over <c>{Dto}.RequiredNavigations</c>, which Mapping works out from
    ///         explicit paths, flattening, nested DTOs and collections. A DTO that genuinely reaches
    ///         through nothing gets an empty list and a method that returns the query untouched — the
    ///         right no-op, and one the caller does not have to know about.
    ///     </para>
    /// </remarks>
    [Fact]
    public void EveryMappedDto_GetsAMethodDrivenByItsOwnNavigations()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string OrderNumber { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            // No property named after a navigation — and it still reaches through one.
            [MapFrom<Order>]
            public partial record OrderFlatDto
            {
                public string OrderNumber { get; init; } = "";

                [MapProperty("Customer.Name")]
                public string CustomerName { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();
        generated.Should().Contain("WithAllRelations");
        generated.Should().Contain("WithIncludesForOrderFlatDto");
        generated.Should().Contain("OrderFlatDto.RequiredNavigations",
            "what to load is the DTO's own list, not a name match this DTO would fail");
    }

    [Fact]
    public void EntityWithNoNavigations_GeneratesNothing()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;

            namespace TestApp;

            public class SimpleBoundary;

            [Entity]
            [BelongsTo<SimpleBoundary>]
            public partial class SimpleEntity
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [MapFrom<SimpleEntity>]
            public partial record SimpleDto
            {
                public string Name { get; init; } = "";
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        // No navigations → no include extensions generated
        generated.Should().BeNull();
    }

    /// <summary>
    ///     A relation across a boundary the entity's boundary does not read has only a key, so there
    ///     is no navigation to include (⇒ <c>01-entita.md</c> 1.29).
    /// </summary>
    [Fact]
    public void CrossBoundaryNavigation_ExcludedFromIncludes()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;
            public class InventoryBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.ManyToOne<Warehouse>.WithNavigation("Warehouse")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<InventoryBoundary>]
            public partial class Warehouse
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            public partial record OrderDetailDto
            {
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();

        // Same-boundary Customer should be included
        generated.Should().Contain("e.Customer");

        // Cross-boundary Warehouse should NOT be included
        generated.Should().NotContain("e.Warehouse");
    }

    [Fact]
    public void GeneratesCorrectHeader()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace MyApp.Catalog;

            public class CatalogBoundary;

            [Entity]
            [BelongsTo<CatalogBoundary>]
            [Relation.OneToMany<Amenity>.WithNavigation("Amenities")]
            public partial class Property
            {
                public System.Guid PersistenceId { get; set; }
                public string Name { get; set; } = "";
            }

            [Entity]
            [BelongsTo<CatalogBoundary>]
            public partial class Amenity
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Property>]
            public partial record PropertyDto
            {
                public ICollection<Amenity> Amenities { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();
        generated.Should().Contain("// Pragmatic.SourceGenerator/Persistence");
        generated.Should().Contain("Include extensions for Property");
    }

    [Fact]
    public void GeneratesCorrectHintName()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace MyApp.Sales;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.OneToMany<LineItem>.WithNavigation("Lines")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            public partial record OrderDto
            {
                public ICollection<LineItem> Lines { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var sources = GeneratorTestHelper.GetGeneratedSourcesAsDictionary(result);
        sources.Keys.Should().Contain(k =>
            k.Contains("Order") && k.Contains("Includes"));
    }

    [Fact]
    public void WithAllRelations_IncludesAllSameBoundaryNavs()
    {
        var source = """
            using Pragmatic.Persistence.Entity;
            using Pragmatic.Mapping.Attributes;
            using System.Collections.Generic;

            namespace TestApp;

            public class SalesBoundary;

            [Entity]
            [BelongsTo<SalesBoundary>]
            [Relation.ManyToOne<Customer>.WithNavigation("Customer")]
            [Relation.OneToMany<LineItem>.WithNavigation("Lines")]
            [Relation.OneToMany<Payment>.WithNavigation("Payments")]
            public partial class Order
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Customer
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class LineItem
            {
                public System.Guid PersistenceId { get; set; }
            }

            [Entity]
            [BelongsTo<SalesBoundary>]
            public partial class Payment
            {
                public System.Guid PersistenceId { get; set; }
            }

            [MapFrom<Order>]
            public partial record OrderDto
            {
                public Customer Customer { get; init; }
            }
            """;

        var result = RunGenerator(source);

        var generated = GeneratorTestHelper.GetGeneratedSource(result, "Includes");
        generated.Should().NotBeNull();

        // WithAllRelations should include all 3 same-boundary navigations
        generated.Should().Contain(".Include(e => e.Customer)");
        generated.Should().Contain(".Include(e => e.Lines)");
        generated.Should().Contain(".Include(e => e.Payments)");
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

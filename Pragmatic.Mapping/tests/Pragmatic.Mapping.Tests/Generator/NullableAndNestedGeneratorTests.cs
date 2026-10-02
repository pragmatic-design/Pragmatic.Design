using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for nullable types and nested DTOs in MappingSourceGenerator.
///     <para>
///         Matrix of combinations:
///         <list type="table">
///             <listheader>
///                 <term>Scenario</term>
///                 <description>Cases</description>
///             </listheader>
///             <item>
///                 <term>Nullable value types</term><description>int?, DateTime?, Guid?</description>
///             </item>
///             <item>
///                 <term>Nullable reference types</term><description>string?</description>
///             </item>
///             <item>
///                 <term>Nested DTO</term><description>Single, Nullable, Collection</description>
///             </item>
///             <item>
///                 <term>Type kinds</term><description>class, record, struct, record struct</description>
///             </item>
///         </list>
///     </para>
/// </summary>
public class NullableAndNestedGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // Nullable Value Types
    // =========================================================================

    [Fact]
    public async Task NullableInt_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public int? OptionalCount { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto
                         {
                             public int Id { get; init; }
                             public int? OptionalCount { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain("OptionalCount = entity.OptionalCount");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task NullableDateTime_MapsDirectly()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public DateTime? DeletedAt { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto
                         {
                             public int Id { get; init; }
                             public DateTime? DeletedAt { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nullable Reference Types
    // =========================================================================

    [Fact]
    public async Task NullableString_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public string? MiddleName { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto
                         {
                             public int Id { get; init; }
                             public string? MiddleName { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nested DTO - Single
    // =========================================================================

    [Fact]
    public async Task NestedDto_MapsWithFromEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Address
                         {
                             public string Street { get; set; } = "";
                             public string City { get; set; } = "";
                         }
                         public class Person
                         {
                             public int Id { get; set; }
                             public Address Address { get; set; } = new();
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string Street { get; init; } = "";
                             public string City { get; init; } = "";
                         }

                         [MapFrom<Test.Entities.Person>]
                         public partial record PersonDto
                         {
                             public int Id { get; init; }
                             public AddressDto Address { get; init; } = null!;
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PersonDto.Mapping");
        mainSource.Should().Contain("AddressDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nested DTO - Nullable
    // =========================================================================

    [Fact]
    public async Task NullableNestedDto_HandlesNullSafely()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Contact
                         {
                             public string Email { get; set; } = "";
                         }
                         public class User
                         {
                             public int Id { get; set; }
                             public Contact? Contact { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Contact>]
                         public partial record ContactDto
                         {
                             public string Email { get; init; } = "";
                         }

                         [MapFrom<Test.Entities.User>]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public ContactDto? Contact { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        // Should have null-safe access
        mainSource.Should().Contain("entity.Contact");
        mainSource.Should().Contain("ContactDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Type Kinds - class (non-record)
    // =========================================================================

    [Fact]
    public async Task PartialClass_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public decimal Price { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Product>]
                         public partial class ProductDto
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public decimal Price { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        mainSource.Should().Contain("public partial class ProductDto");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Type Kinds - struct (non-record)
    // =========================================================================

    [Fact]
    public async Task PartialStruct_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public struct Point
                         {
                             public int X { get; set; }
                             public int Y { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Point>]
                         public partial struct PointDto
                         {
                             public int X { get; set; }
                             public int Y { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PointDto.Mapping");
        mainSource.Should().Contain("public partial struct PointDto");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Mixed: Nullable + Collection
    // =========================================================================

    [Fact]
    public async Task NullableListOfNullableInt_HandlesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public List<int?>? Scores { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Entity>]
                         public partial record Dto
                         {
                             public int Id { get; init; }
                             public List<int?>? Scores { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Dto.Mapping");
        mainSource.Should().Contain("Scores");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Complex: Nested DTO in Collection
    // =========================================================================

    [Fact]
    public async Task ArrayOfNestedDto_MapsEachElement()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Tag { public string Name { get; set; } = ""; }
                         public class Article
                         {
                             public int Id { get; set; }
                             public Tag[] Tags { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Tag>]
                         public partial record TagDto { public string Name { get; init; } = ""; }

                         [MapFrom<Test.Entities.Article>]
                         public partial record ArticleDto
                         {
                             public int Id { get; init; }
                             public TagDto[] Tags { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ArticleDto.Mapping");
        mainSource.Should().Contain(".Select(");
        mainSource.Should().Contain("TagDto.FromEntity");
        mainSource.Should().Contain(".ToArray()");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Edge Case: Nullable Collection of Nullable Nested DTO
    // =========================================================================

    [Fact]
    public async Task NullableListOfNullableNestedDto_HandlesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Phone { public string Number { get; set; } = ""; }
                         public class Contact
                         {
                             public int Id { get; set; }
                             public List<Phone?>? Phones { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Phone>]
                         public partial record PhoneDto { public string Number { get; init; } = ""; }

                         [MapFrom<Test.Entities.Contact>]
                         public partial record ContactDto
                         {
                             public int Id { get; init; }
                             public List<PhoneDto?>? Phones { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nested DTO - ToEntity (DTO → Entity) - P0 Coverage
    // Tests verify that generator correctly calls nested DTO's ToEntity().
    // =========================================================================

    [Fact]
    public async Task NestedDto_MapTo_GeneratesToEntityWithNestedCall()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Address
                         {
                             public string Street { get; set; } = "";
                             public string City { get; set; } = "";
                         }
                         public class User
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Address? Address { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string Street { get; init; } = "";
                             public string City { get; init; } = "";
                         }

                         [MapTo<Test.Entities.User>]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public AddressDto? Address { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var userSource = GetGeneratedSource(result, "UserDto.Mapping");
        userSource.Should().NotBeNull();

        // ToEntity should call nested DTO's ToEntity
        userSource.Should().Contain("ToEntity");
        // Should handle nullable nested DTO (Address is nullable)
        userSource.Should().Contain("Address");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task NestedDto_MapTo_GeneratesToEntityWithNestedCall_WithContact()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Contact
                         {
                             public string Phone { get; set; } = "";
                             public string Email { get; set; } = "";
                         }
                         public class Customer
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Contact Contact { get; set; } = new();
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Contact>]
                         public partial record ContactDto
                         {
                             public string Phone { get; init; } = "";
                             public string Email { get; init; } = "";
                         }

                         [MapTo<Test.Entities.Customer>]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public ContactDto Contact { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var customerSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        customerSource.Should().NotBeNull();

        // ToEntity should handle nested DTO
        customerSource.Should().Contain("ToEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task CollectionOfNestedDto_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class OrderLine
                         {
                             public int Id { get; set; }
                             public string Product { get; set; } = "";
                             public int Quantity { get; set; }
                         }
                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public List<OrderLine> Lines { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.OrderLine>]
                         public partial record OrderLineDto
                         {
                             public int Id { get; init; }
                             public string Product { get; init; } = "";
                             public int Quantity { get; init; }
                         }

                         [MapTo<Test.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";
                             public List<OrderLineDto> Lines { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var orderSource = GetGeneratedSource(result, "OrderDto.Mapping");
        orderSource.Should().NotBeNull();

        // ToEntity should handle collection of nested DTOs
        orderSource.Should().Contain("ToEntity");
        orderSource.Should().Contain("Lines");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task BidirectionalNested_MapFromAndMapTo_GeneratesBothDirections()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Settings
                         {
                             public bool NotificationsEnabled { get; set; }
                             public string Theme { get; set; } = "light";
                         }
                         public class Profile
                         {
                             public int Id { get; set; }
                             public string Username { get; set; } = "";
                             public Settings Settings { get; set; } = new();
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Settings>]
                         [MapTo<Test.Entities.Settings>]
                         public partial record SettingsDto
                         {
                             public bool NotificationsEnabled { get; init; }
                             public string Theme { get; init; } = "light";
                         }

                         [MapFrom<Test.Entities.Profile>]
                         [MapTo<Test.Entities.Profile>]
                         public partial record ProfileDto
                         {
                             public int Id { get; init; }
                             public string Username { get; init; } = "";
                             public SettingsDto Settings { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var profileSource = GetGeneratedSource(result, "ProfileDto.Mapping");
        profileSource.Should().NotBeNull();

        // Should have both FromEntity and ToEntity
        profileSource.Should().Contain("FromEntity");
        profileSource.Should().Contain("ToEntity");
        profileSource.Should().Contain("Settings");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Deep Nesting (3+ Levels) - P1 Coverage
    // Tests verify that generator handles multi-level nesting correctly.
    // User → Orders → Lines (3 levels)
    // =========================================================================

    [Fact]
    public async Task DeepNesting_ThreeLevels_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class OrderLine
                         {
                             public int Id { get; set; }
                             public string ProductName { get; set; } = "";
                             public int Quantity { get; set; }
                             public decimal UnitPrice { get; set; }
                         }
                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public decimal Total { get; set; }
                             public List<OrderLine> Lines { get; set; } = [];
                         }
                         public class Customer
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public List<Order> Orders { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.OrderLine>]
                         public partial record OrderLineDto
                         {
                             public int Id { get; init; }
                             public string ProductName { get; init; } = "";
                             public int Quantity { get; init; }
                             public decimal UnitPrice { get; init; }
                         }

                         [MapFrom<Test.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";
                             public decimal Total { get; init; }
                             public List<OrderLineDto> Lines { get; init; } = [];
                         }

                         [MapFrom<Test.Entities.Customer>]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public List<OrderDto> Orders { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Customer → Orders → Lines (3 levels)
        var customerSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        customerSource.Should().NotBeNull();
        customerSource.Should().Contain("OrderDto.FromEntity");

        var orderSource = GetGeneratedSource(result, "OrderDto.Mapping");
        orderSource.Should().NotBeNull();
        orderSource.Should().Contain("OrderLineDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task DeepNesting_ThreeLevels_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class LineItem
                         {
                             public int Id { get; set; }
                             public string Sku { get; set; } = "";
                             public int Qty { get; set; }
                         }
                         public class Invoice
                         {
                             public int Id { get; set; }
                             public string InvoiceNumber { get; set; } = "";
                             public List<LineItem> Items { get; set; } = [];
                         }
                         public class Account
                         {
                             public int Id { get; set; }
                             public string AccountName { get; set; } = "";
                             public List<Invoice> Invoices { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.LineItem>]
                         public partial record LineItemDto
                         {
                             public int Id { get; init; }
                             public string Sku { get; init; } = "";
                             public int Qty { get; init; }
                         }

                         [MapTo<Test.Entities.Invoice>]
                         public partial record InvoiceDto
                         {
                             public int Id { get; init; }
                             public string InvoiceNumber { get; init; } = "";
                             public List<LineItemDto> Items { get; init; } = [];
                         }

                         [MapTo<Test.Entities.Account>]
                         public partial record AccountDto
                         {
                             public int Id { get; init; }
                             public string AccountName { get; init; } = "";
                             public List<InvoiceDto> Invoices { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Account → Invoices → Items (3 levels) - ToEntity
        var accountSource = GetGeneratedSource(result, "AccountDto.Mapping");
        accountSource.Should().NotBeNull();
        accountSource.Should().Contain("ToEntity");
        accountSource.Should().Contain("Invoices");

        var invoiceSource = GetGeneratedSource(result, "InvoiceDto.Mapping");
        invoiceSource.Should().NotBeNull();
        invoiceSource.Should().Contain("ToEntity");
        invoiceSource.Should().Contain("Items");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task DeepNesting_FourLevels_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Attribute
                         {
                             public string Key { get; set; } = "";
                             public string Value { get; set; } = "";
                         }
                         public class Variant
                         {
                             public int Id { get; set; }
                             public string Sku { get; set; } = "";
                             public List<Attribute> Attributes { get; set; } = [];
                         }
                         public class Product
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public List<Variant> Variants { get; set; } = [];
                         }
                         public class Catalog
                         {
                             public int Id { get; set; }
                             public string CatalogName { get; set; } = "";
                             public List<Product> Products { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Attribute>]
                         public partial record AttributeDto
                         {
                             public string Key { get; init; } = "";
                             public string Value { get; init; } = "";
                         }

                         [MapFrom<Test.Entities.Variant>]
                         public partial record VariantDto
                         {
                             public int Id { get; init; }
                             public string Sku { get; init; } = "";
                             public List<AttributeDto> Attributes { get; init; } = [];
                         }

                         [MapFrom<Test.Entities.Product>]
                         public partial record ProductDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public List<VariantDto> Variants { get; init; } = [];
                         }

                         [MapFrom<Test.Entities.Catalog>]
                         public partial record CatalogDto
                         {
                             public int Id { get; init; }
                             public string CatalogName { get; init; } = "";
                             public List<ProductDto> Products { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Catalog → Products → Variants → Attributes (4 levels)
        var catalogSource = GetGeneratedSource(result, "CatalogDto.Mapping");
        catalogSource.Should().NotBeNull();
        catalogSource.Should().Contain("ProductDto.FromEntity");

        var productSource = GetGeneratedSource(result, "ProductDto.Mapping");
        productSource.Should().NotBeNull();
        productSource.Should().Contain("VariantDto.FromEntity");

        var variantSource = GetGeneratedSource(result, "VariantDto.Mapping");
        variantSource.Should().NotBeNull();
        variantSource.Should().Contain("AttributeDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task DeepNesting_FourLevels_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Mutation;

                     namespace Test.Entities
                     {
                         public class Spec
                         {
                             public string Key { get; set; } = "";
                             public string Value { get; set; } = "";
                         }
                         public class Option
                         {
                             public int Id { get; set; }
                             public string Label { get; set; } = "";
                             public List<Spec> Specs { get; set; } = [];
                         }
                         public class Item
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public List<Option> Options { get; set; } = [];
                         }
                         public class Cart
                         {
                             public int Id { get; set; }
                             public string SessionId { get; set; } = "";
                             public List<Item> Items { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Spec>]
                         public partial record SpecDto
                         {
                             public string Key { get; init; } = "";
                             public string Value { get; init; } = "";
                         }

                         [MapTo<Test.Entities.Option>]
                         public partial record OptionDto
                         {
                             public int Id { get; init; }
                             public string Label { get; init; } = "";
                             // Spec has no identity of its own — key/value, not a row. Rebuilding is
                             // what updating it means, and saying so is what PRAG0333 asks for.
                             [CollectionStrategy(CollectionStrategy.Replace)]
                             public List<SpecDto> Specs { get; init; } = [];
                         }

                         [MapTo<Test.Entities.Item>]
                         public partial record ItemDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public List<OptionDto> Options { get; init; } = [];
                         }

                         [MapTo<Test.Entities.Cart>]
                         public partial record CartDto
                         {
                             public int Id { get; init; }
                             public string SessionId { get; init; } = "";
                             public List<ItemDto> Items { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Cart → Items → Options → Specs (4 levels) - ToEntity
        var cartSource = GetGeneratedSource(result, "CartDto.Mapping");
        cartSource.Should().NotBeNull();
        cartSource.Should().Contain("ToEntity");
        cartSource.Should().Contain("Items");

        var itemSource = GetGeneratedSource(result, "ItemDto.Mapping");
        itemSource.Should().NotBeNull();
        itemSource.Should().Contain("ToEntity");
        itemSource.Should().Contain("Options");

        var optionSource = GetGeneratedSource(result, "OptionDto.Mapping");
        optionSource.Should().NotBeNull();
        optionSource.Should().Contain("ToEntity");
        optionSource.Should().Contain("Specs");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task DeepNesting_MixedSingleAndCollection_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Country
                         {
                             public string Code { get; set; } = "";
                             public string Name { get; set; } = "";
                         }
                         public class Address
                         {
                             public string Street { get; set; } = "";
                             public string City { get; set; } = "";
                             public Country Country { get; set; } = new();
                         }
                         public class Branch
                         {
                             public int Id { get; set; }
                             public string BranchName { get; set; } = "";
                             public Address Address { get; set; } = new();
                         }
                         public class Company
                         {
                             public int Id { get; set; }
                             public string CompanyName { get; set; } = "";
                             public List<Branch> Branches { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Country>]
                         public partial record CountryDto
                         {
                             public string Code { get; init; } = "";
                             public string Name { get; init; } = "";
                         }

                         [MapFrom<Test.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string Street { get; init; } = "";
                             public string City { get; init; } = "";
                             public CountryDto Country { get; init; } = null!;
                         }

                         [MapFrom<Test.Entities.Branch>]
                         public partial record BranchDto
                         {
                             public int Id { get; init; }
                             public string BranchName { get; init; } = "";
                             public AddressDto Address { get; init; } = null!;
                         }

                         [MapFrom<Test.Entities.Company>]
                         public partial record CompanyDto
                         {
                             public int Id { get; init; }
                             public string CompanyName { get; init; } = "";
                             public List<BranchDto> Branches { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Company → Branches (collection) → Address (single) → Country (single) = 4 levels, mixed
        var companySource = GetGeneratedSource(result, "CompanyDto.Mapping");
        companySource.Should().NotBeNull();
        companySource.Should().Contain("BranchDto.FromEntity");

        var branchSource = GetGeneratedSource(result, "BranchDto.Mapping");
        branchSource.Should().NotBeNull();
        branchSource.Should().Contain("AddressDto.FromEntity");

        var addressSource = GetGeneratedSource(result, "AddressDto.Mapping");
        addressSource.Should().NotBeNull();
        addressSource.Should().Contain("CountryDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task DeepNesting_WithNullableIntermediateLevel_HandlesNullSafely()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class GeoLocation
                         {
                             public double Latitude { get; set; }
                             public double Longitude { get; set; }
                         }
                         public class Warehouse
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public GeoLocation? Location { get; set; }
                         }
                         public class Supplier
                         {
                             public int Id { get; set; }
                             public string SupplierName { get; set; } = "";
                             public Warehouse? MainWarehouse { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.GeoLocation>]
                         public partial record GeoLocationDto
                         {
                             public double Latitude { get; init; }
                             public double Longitude { get; init; }
                         }

                         [MapFrom<Test.Entities.Warehouse>]
                         public partial record WarehouseDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public GeoLocationDto? Location { get; init; }
                         }

                         [MapFrom<Test.Entities.Supplier>]
                         public partial record SupplierDto
                         {
                             public int Id { get; init; }
                             public string SupplierName { get; init; } = "";
                             public WarehouseDto? MainWarehouse { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        // Supplier → MainWarehouse? → Location? (3 levels, all nullable)
        var supplierSource = GetGeneratedSource(result, "SupplierDto.Mapping");
        supplierSource.Should().NotBeNull();
        supplierSource.Should().Contain("WarehouseDto");

        var warehouseSource = GetGeneratedSource(result, "WarehouseDto.Mapping");
        warehouseSource.Should().NotBeNull();
        warehouseSource.Should().Contain("GeoLocationDto");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P2: Self-Referencing / Circular Reference
    // Tests verify that generator handles self-referencing types correctly.
    // Common scenarios: Tree nodes, Employee → Manager, Category → Parent
    // =========================================================================

    [Fact]
    public async Task SelfReferencing_TreeNode_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class TreeNode
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public TreeNode? Parent { get; set; }
                             public List<TreeNode> Children { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.TreeNode>]
                         public partial record TreeNodeDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public TreeNodeDto? Parent { get; init; }
                             public List<TreeNodeDto> Children { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var nodeSource = GetGeneratedSource(result, "TreeNodeDto.Mapping");
        nodeSource.Should().NotBeNull();
        // Self-referencing should call its own FromEntity
        nodeSource.Should().Contain("TreeNodeDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task SelfReferencing_EmployeeManager_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Employee
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Employee? Manager { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Employee>]
                         public partial record EmployeeDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public EmployeeDto? Manager { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var employeeSource = GetGeneratedSource(result, "EmployeeDto.Mapping");
        employeeSource.Should().NotBeNull();
        // Self-referencing nullable: Manager
        employeeSource.Should().Contain("EmployeeDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task SelfReferencing_CategoryHierarchy_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Category
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Category? ParentCategory { get; set; }
                             public List<Category> SubCategories { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Category>]
                         public partial record CategoryDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public CategoryDto? ParentCategory { get; init; }
                             public List<CategoryDto> SubCategories { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var categorySource = GetGeneratedSource(result, "CategoryDto.Mapping");
        categorySource.Should().NotBeNull();
        categorySource.Should().Contain("ToEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task SelfReferencing_Circular_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class OrgUnit
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public OrgUnit? Parent { get; set; }
                             public List<OrgUnit> Children { get; set; } = [];
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.OrgUnit>]
                         [MapTo<Test.Entities.OrgUnit>]
                         public partial record OrgUnitDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public OrgUnitDto? Parent { get; init; }
                             public List<OrgUnitDto> Children { get; init; } = [];
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var source2 = GetGeneratedSource(result, "OrgUnitDto.Mapping");
        source2.Should().NotBeNull();

        // FromEntity should use visited set for circular reference protection
        source2.Should().Contain("FromEntity");

        // ToEntity should also be generated for bidirectional mapping
        source2.Should().Contain("ToEntity");

        // ApplyTo should be generated
        source2.Should().Contain("ApplyTo");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P2: Required Properties (C# 11+)
    // Tests verify that generator handles required properties correctly.
    // =========================================================================

    [Fact]
    public async Task RequiredProperty_InEntity_MapsCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Document
                         {
                             public int Id { get; set; }
                             public required string Title { get; set; }
                             public required string Content { get; set; }
                             public string? Description { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Document>]
                         public partial record DocumentDto
                         {
                             public int Id { get; init; }
                             public required string Title { get; init; }
                             public required string Content { get; init; }
                             public string? Description { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var docSource = GetGeneratedSource(result, "DocumentDto.Mapping");
        docSource.Should().NotBeNull();
        docSource.Should().Contain("Title = entity.Title");
        docSource.Should().Contain("Content = entity.Content");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task RequiredProperty_InDto_MapTo_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Article
                         {
                             public int Id { get; set; }
                             public required string Headline { get; set; }
                             public required string Body { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Article>]
                         public partial record ArticleDto
                         {
                             public int Id { get; init; }
                             public required string Headline { get; init; }
                             public required string Body { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var articleSource = GetGeneratedSource(result, "ArticleDto.Mapping");
        articleSource.Should().NotBeNull();
        articleSource.Should().Contain("ToEntity");
        articleSource.Should().Contain("Headline");
        articleSource.Should().Contain("Body");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P2: Global Namespace
    // Tests verify that generator works with types in global namespace.
    // =========================================================================

    [Fact]
    public async Task GlobalNamespace_Entity_MapsCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     // Entity in global namespace
                     public class GlobalEntity
                     {
                         public int Id { get; set; }
                         public string Value { get; set; } = "";
                     }

                     namespace Test.Dtos
                     {
                         [MapFrom<GlobalEntity>]
                         public partial record GlobalEntityDto
                         {
                             public int Id { get; init; }
                             public string Value { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "GlobalEntityDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("global::GlobalEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task GlobalNamespace_Dto_MapsCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class SimpleEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }
                     }

                     // DTO in global namespace
                     [MapFrom<Test.Entities.SimpleEntity>]
                     public partial record GlobalDto
                     {
                         public int Id { get; init; }
                         public string Name { get; init; } = "";
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "GlobalDto.Mapping");
        dtoSource.Should().NotBeNull();
        // Global namespace DTOs should not have namespace declaration
        dtoSource.Should().Contain("partial record GlobalDto");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task GlobalNamespace_BothEntityAndDto_MapsCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     // Both entity and DTO in global namespace
                     public class RootEntity
                     {
                         public int Id { get; set; }
                         public string Data { get; set; } = "";
                     }

                     [MapFrom<RootEntity>]
                     [MapTo<RootEntity>]
                     public partial record RootDto
                     {
                         public int Id { get; init; }
                         public string Data { get; init; } = "";
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "RootDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("FromEntity");
        dtoSource.Should().Contain("ToEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // P2: Interface / Abstract Source
    // Tests verify that generator handles interface and abstract class sources.
    // =========================================================================

    [Fact]
    public async Task InterfaceSource_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public interface IEntity
                         {
                             int Id { get; }
                             string Name { get; }
                         }

                         public class ConcreteEntity : IEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.IEntity>]
                         public partial record EntityDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "EntityDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("IEntity entity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task AbstractSource_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public abstract class BaseEntity
                         {
                             public int Id { get; set; }
                             public abstract string Name { get; set; }
                         }

                         public class DerivedEntity : BaseEntity
                         {
                             public override string Name { get; set; } = "";
                             public string Extra { get; set; } = "";
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.BaseEntity>]
                         public partial record BaseEntityDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "BaseEntityDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("BaseEntity entity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InterfaceWithMultipleProperties_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public interface IProduct
                         {
                             int Id { get; }
                             string Sku { get; }
                             string Name { get; }
                             decimal Price { get; }
                             bool IsActive { get; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.IProduct>]
                         public partial record ProductDto
                         {
                             public int Id { get; init; }
                             public string Sku { get; init; } = "";
                             public string Name { get; init; } = "";
                             public decimal Price { get; init; }
                             public bool IsActive { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "ProductDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("entity.Sku");
        dtoSource.Should().Contain("entity.Price");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task InterfaceSource_WithProjection_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public interface ICustomer
                         {
                             int Id { get; }
                             string Email { get; }
                             string FullName { get; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.ICustomer>]
                         [GenerateProjection]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string Email { get; init; } = "";
                             public string FullName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        dtoSource.Should().NotBeNull();
        dtoSource.Should().Contain("Expression<Func<");
        dtoSource.Should().Contain("Projection");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // TargetPath for Nested Entity Assignment
    // =========================================================================

    [Fact]
    public async Task MapTo_WithTargetPath_GeneratesNestedAssignment()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Customer
                         {
                             public string Name { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public string Description { get; set; } = "";
                             public Customer Customer { get; set; } = new();
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Order>]
                         public partial record UpdateOrderDto
                         {
                             public string Description { get; init; } = "";

                             // This maps to entity.Customer.Name
                             [MapProperty(Target = "Customer.Name")]
                             public string CustomerName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        dtoSource.Should().NotBeNull();

        // ToEntity should create entity and assign nested property after
        dtoSource.Should().Contain("var entity = new global::Test.Entities.Order()");
        dtoSource.Should().Contain("entity.Customer.Name = this.CustomerName;");
        dtoSource.Should().Contain("return entity;");

        // ApplyTo should assign to nested path
        dtoSource.Should().Contain("public void ApplyTo");
        dtoSource.Should().Contain("entity.Customer.Name = this.CustomerName;");

        // The intermediate navigation is null-guarded: without this, a null Customer would NRE.
        dtoSource.Should().Contain("entity.Customer ??= new();");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public void MapTo_WithDeepTargetPath_InitializesEveryIntermediate()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Address { public string City { get; set; } = ""; }
                         public class Customer { public Address Address { get; set; } = new(); }
                         public class Order
                         {
                             public string Description { get; set; } = "";
                             public Customer Customer { get; set; } = new();
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Order>]
                         public partial record UpdateOrderDto
                         {
                             public string Description { get; init; } = "";

                             [MapProperty(Target = "Customer.Address.City")]
                             public string City { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var dtoSource = GetGeneratedSource(result, "UpdateOrderDto.Mapping");
        dtoSource.Should().Contain("entity.Customer ??= new();")
            .And.Contain("entity.Customer.Address ??= new();")
            .And.Contain("entity.Customer.Address.City = this.City;");
    }

    [Fact]
    public void MapTo_WithInvalidTargetPath_ReportsPRAG0302_AndStillCompiles()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Order { public string Description { get; set; } = ""; }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Order>]
                         public partial record UpdateOrderDto
                         {
                             public string Description { get; init; } = "";

                             // "Nope" does not exist on Order — must surface as PRAG0302, not a CS error
                             // in the generated code.
                             [MapProperty(Target = "Nope.Name")]
                             public string CustomerName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasDiagnostic(result, "PRAG0302").Should().BeTrue("an invalid Target segment must be a clear PRAG error");
        HasCompilationErrors(result).Should().BeFalse(
            "the invalid mapping is skipped: " + string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));
    }
}
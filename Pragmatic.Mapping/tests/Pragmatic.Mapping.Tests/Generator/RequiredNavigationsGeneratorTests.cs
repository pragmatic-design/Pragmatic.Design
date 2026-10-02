using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for RequiredNavigations inference — DTOs declare which entity
///     navigation paths need to be eagerly loaded for their mapping.
/// </summary>
public class RequiredNavigationsGeneratorTests : MappingGeneratorTestBase
{
    [Fact]
    public void DirectMatch_NoNavigation_DoesNotGenerateRequiredNavigations()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public decimal Total { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";
                             public decimal Total { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // No navigations needed — only direct properties. The member is always emitted on a [MapFrom]
        // DTO, empty when there is nothing to load, so generated code can name it without first
        // knowing whether it exists; what is under test is that the list is empty, not that the
        // property is missing.
        generated.Should().Contain("RequiredNavigations { get; } = [];");
    }

    [Fact]
    public void ExplicitMapProperty_MultiSegment_GeneratesNavigation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public string Name { get; set; } = "";
                             public string Email { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public Customer? Customer { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";

                             [MapProperty("Customer.Name")]
                             public string? CustomerName { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"Customer\"");
    }

    [Fact]
    public void FlatteningConvention_InfersNavigation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Address
                         {
                             public string City { get; set; } = "";
                             public string Country { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public Address? Address { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string? AddressCity { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Flattening convention: AddressCity → Address.City → navigation "Address"
        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"Address\"");
    }

    [Fact]
    public void NestedDto_GeneratesNavigation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Address
                         {
                             public string City { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public Address? ShippingAddress { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string City { get; init; } = "";
                         }

                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public AddressDto? ShippingAddress { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Nested DTO property → navigation "ShippingAddress"
        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"ShippingAddress\"");
    }

    [Fact]
    public void CollectionDto_GeneratesNavigation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
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
                             public List<OrderLine> Lines { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.OrderLine>]
                         public partial record OrderLineDto
                         {
                             public int Id { get; init; }
                             public string Product { get; init; } = "";
                             public int Quantity { get; init; }
                         }

                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public List<OrderLineDto> Lines { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Collection of DTOs → navigation "Lines"
        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"Lines\"");
    }

    [Fact]
    public void DeepPath_GeneratesIntermediateNavigations()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Country
                         {
                             public string Name { get; set; } = "";
                         }

                         public class Address
                         {
                             public string City { get; set; } = "";
                             public Country? Country { get; set; }
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public Address? Address { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }

                             [MapProperty("Address.Country.Name")]
                             public string? CountryName { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Deep path: Address.Country.Name → navigations "Address" and "Address.Country"
        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"Address\"");
        generated.Should().Contain("\"Address.Country\"");
    }

    [Fact]
    public void CombinedScenario_MultipleNavigationTypes()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public string Name { get; set; } = "";
                         }

                         public class OrderLine
                         {
                             public int Id { get; set; }
                             public string Product { get; set; } = "";
                         }

                         public class Address
                         {
                             public string City { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public Customer? Customer { get; set; }
                             public Address? ShippingAddress { get; set; }
                             public List<OrderLine> Lines { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string City { get; init; } = "";
                         }

                         [MapFrom<TestApp.Entities.OrderLine>]
                         public partial record OrderLineDto
                         {
                             public int Id { get; init; }
                             public string Product { get; init; } = "";
                         }

                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDetailDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";

                             [MapProperty("Customer.Name")]
                             public string? CustomerName { get; init; }

                             public AddressDto? ShippingAddress { get; init; }
                             public List<OrderLineDto> Lines { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDetailDto.Mapping");
        generated.Should().NotBeNull();

        // Combined: explicit path + nested DTO + collection DTO
        generated.Should().Contain("RequiredNavigations");
        generated.Should().Contain("\"Customer\"");
        generated.Should().Contain("\"Lines\"");
        generated.Should().Contain("\"ShippingAddress\"");
    }

    [Fact]
    public void RequiredNavigations_IsSortedAlphabetically()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public string Name { get; set; } = "";
                         }

                         public class OrderLine
                         {
                             public int Id { get; set; }
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public Customer? Customer { get; set; }
                             public List<OrderLine> Lines { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.OrderLine>]
                         public partial record OrderLineDto
                         {
                             public int Id { get; init; }
                         }

                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }

                             [MapProperty("Customer.Name")]
                             public string? CustomerName { get; init; }

                             public List<OrderLineDto> Lines { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Should be sorted: "Customer" before "Lines"
        var customerIdx = generated!.IndexOf("\"Customer\"", StringComparison.Ordinal);
        var linesIdx = generated.IndexOf("\"Lines\"", StringComparison.Ordinal);
        customerIdx.Should().BeLessThan(linesIdx, "navigations should be sorted alphabetically");
    }

    [Fact]
    public void CollectionOfScalars_NoNavigation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public List<string> Tags { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public List<string> Tags { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Collection of scalars (List<string>) is NOT a navigation — nothing to Include, so the list
        // is empty rather than absent.
        generated.Should().Contain("RequiredNavigations { get; } = [];");
    }

    [Fact]
    public void RequiredNavigations_DeduplicatesFromMultipleProperties()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Customer
                         {
                             public string Name { get; set; } = "";
                             public string Email { get; set; } = "";
                         }

                         public class Order
                         {
                             public int Id { get; set; }
                             public Customer? Customer { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }

                             [MapProperty("Customer.Name")]
                             public string? CustomerName { get; init; }

                             [MapProperty("Customer.Email")]
                             public string? CustomerEmail { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        generated.Should().Contain("RequiredNavigations");
        // "Customer" should appear only once (deduplicated)
        var count = generated!.Split("\"Customer\"").Length - 1;
        count.Should().Be(1, "Customer navigation should be deduplicated");
    }
}

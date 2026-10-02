using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for [GenerateBodyOnlyVariant] — generates FromEntityBodyOnly()
///     that maps only scalar properties, skipping collections/nested DTOs/dictionaries.
/// </summary>
public class BodyOnlyVariantGeneratorTests : MappingGeneratorTestBase
{
    /// <summary>
    ///     Extracts just the FromEntityBodyOnly method section from generated code,
    ///     excluding subsequent members like MappingContext struct and Selector property.
    /// </summary>
    private static string ExtractBodyOnlySection(string generated)
    {
        var start = generated.IndexOf("FromEntityBodyOnly(", StringComparison.Ordinal);
        // The method ends before "// Partial methods" comment
        var end = generated.IndexOf("// Partial methods", start, StringComparison.Ordinal);
        return end > start ? generated[start..end] : generated[start..];
    }

    [Fact]
    public void BodyOnlyVariant_WithScalarsAndCollection_GeneratesBothMethods()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public string OrderNumber { get; set; } = "";
                             public decimal Total { get; set; }
                             public List<string> Tags { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Order>]
                         [GenerateBodyOnlyVariant]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string OrderNumber { get; init; } = "";
                             public decimal Total { get; init; }
                             public List<string> Tags { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "OrderDto.Mapping");
        generated.Should().NotBeNull();

        // Both methods should exist
        generated.Should().Contain("FromEntity(");
        generated.Should().Contain("FromEntityBodyOnly(");

        // FromEntityBodyOnly should contain scalar properties only
        var bodyOnlySection = ExtractBodyOnlySection(generated!);

        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.OrderNumber");
        bodyOnlySection.Should().Contain("entity.Total");

        // FromEntityBodyOnly should NOT contain collection properties
        bodyOnlySection.Should().NotContain("Tags");
    }

    [Fact]
    public void BodyOnlyVariant_WithNestedDto_ExcludesNestedFromBodyOnly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Address
                         {
                             public string City { get; set; } = "";
                         }

                         public class Customer
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Address ShippingAddress { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Address>]
                         public partial record AddressDto
                         {
                             public string City { get; init; } = "";
                         }

                         [MapFrom<TestApp.Entities.Customer>]
                         [GenerateBodyOnlyVariant]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public AddressDto? ShippingAddress { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "CustomerDto.Mapping");
        generated.Should().NotBeNull();

        // FromEntity should contain the nested DTO
        var fromEntityStart = generated!.IndexOf("FromEntity(", StringComparison.Ordinal);
        var fromEntitySection = generated[fromEntityStart..generated.IndexOf("FromEntityBodyOnly", StringComparison.Ordinal)];
        fromEntitySection.Should().Contain("ShippingAddress");

        // FromEntityBodyOnly should NOT contain the nested DTO
        var bodyOnlySection = ExtractBodyOnlySection(generated);
        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.Name");
        bodyOnlySection.Should().NotContain("ShippingAddress");
    }

    [Fact]
    public void BodyOnlyVariant_WithoutAttribute_DoesNotGenerateBodyOnly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Product>]
                         public partial record ProductDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "ProductDto.Mapping");
        generated.Should().NotBeNull();
        generated.Should().Contain("FromEntity(");
        generated.Should().NotContain("FromEntityBodyOnly");
    }

    [Fact]
    public void BodyOnlyVariant_AllScalar_BodyOnlyMatchesFromEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class SimpleEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public decimal Price { get; set; }
                             public bool IsActive { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.SimpleEntity>]
                         [GenerateBodyOnlyVariant]
                         public partial record SimpleDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public decimal Price { get; init; }
                             public bool IsActive { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "SimpleDto.Mapping");
        generated.Should().NotBeNull();

        // Both methods should exist with all properties
        var bodyOnlySection = ExtractBodyOnlySection(generated!);

        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.Name");
        bodyOnlySection.Should().Contain("entity.Price");
        bodyOnlySection.Should().Contain("entity.IsActive");
    }

    [Fact]
    public void BodyOnlyVariant_WithDictionary_ExcludesDictionaryFromBodyOnly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Config
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Dictionary<string, string> Settings { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Config>]
                         [GenerateBodyOnlyVariant]
                         public partial record ConfigDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public Dictionary<string, string>? Settings { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "ConfigDto.Mapping");
        generated.Should().NotBeNull();

        var bodyOnlySection = ExtractBodyOnlySection(generated!);

        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.Name");
        bodyOnlySection.Should().NotContain("Settings");
    }

    [Fact]
    public void BodyOnlyVariant_WithMapIgnore_RespectIgnore()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Item
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public string Secret { get; set; } = "";
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Item>]
                         [GenerateBodyOnlyVariant]
                         public partial record ItemDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             [MapIgnore]
                             public string Secret { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "ItemDto.Mapping");
        generated.Should().NotBeNull();

        var bodyOnlySection = ExtractBodyOnlySection(generated!);

        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.Name");
        bodyOnlySection.Should().NotContain("Secret");
    }

    [Fact]
    public void BodyOnlyVariant_IsStaticMethod()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Invoice
                         {
                             public int Id { get; set; }
                             public string Number { get; set; } = "";
                             public List<string> Lines { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Invoice>]
                         [GenerateBodyOnlyVariant]
                         public partial record InvoiceDto
                         {
                             public int Id { get; init; }
                             public string Number { get; init; } = "";
                             public List<string> Lines { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse();

        var generated = GetGeneratedSource(result, "InvoiceDto.Mapping");
        generated.Should().NotBeNull();
        generated.Should().Contain("public static InvoiceDto FromEntityBodyOnly(");
    }

    [Fact]
    public void BodyOnlyVariant_WithClass_WorksWithNonRecord()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using System.Collections.Generic;

                     namespace TestApp.Entities
                     {
                         public class Task
                         {
                             public int Id { get; set; }
                             public string Title { get; set; } = "";
                             public List<string> Labels { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Task>]
                         [GenerateBodyOnlyVariant]
                         public partial class TaskDto
                         {
                             public int Id { get; set; }
                             public string Title { get; set; } = "";
                             public List<string> Labels { get; set; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var generated = GetGeneratedSource(result, "TaskDto.Mapping");
        generated.Should().NotBeNull();
        generated.Should().Contain("FromEntityBodyOnly(");

        // Class uses "new TaskDto()" with parentheses
        var bodyOnlySection = ExtractBodyOnlySection(generated!);
        bodyOnlySection.Should().Contain("entity.Id");
        bodyOnlySection.Should().Contain("entity.Title");
        bodyOnlySection.Should().NotContain("Labels");
    }
}

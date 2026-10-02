using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Snapshot tests for advanced MappingSourceGenerator scenarios.
/// </summary>
public class AdvancedMappingGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // [MapProperty] Explicit Path Tests
    // =========================================================================

    [Fact]
    public async Task MapProperty_ExplicitPath_MapsToSpecifiedSource()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Employee
                         {
                             public int Id { get; set; }
                             public string FirstName { get; set; } = "";
                             public string Surname { get; set; } = "";
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Employee>]
                         public partial record EmployeeDto
                         {
                             public int Id { get; init; }
                             public string FirstName { get; init; } = "";
                             // Map to different source property
                             [MapProperty("Surname")]
                             public string LastName { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EmployeeDto.Mapping");
        mainSource.Should().Contain("entity.Surname");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task MapProperty_NestedPath_MapsFromNestedProperty()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class ContactInfo
                         {
                             public string Email { get; set; } = "";
                             public string Phone { get; set; } = "";
                         }

                         public class Customer
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public ContactInfo? Contact { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Customer>]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             [MapProperty("Contact.Email")]
                             public string? Email { get; init; }
                             [MapProperty("Contact.Phone")]
                             public string? Phone { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        mainSource.Should().Contain("entity.Contact");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // [MapIgnore] Tests
    // =========================================================================

    [Fact]
    public async Task MapIgnore_ExcludesProperty()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Secret
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public string Password { get; set; } = "";
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Secret>]
                         public partial record SecretDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             [MapIgnore]
                             public string Password { get; init; } = "***";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SecretDto.Mapping");
        // Password should not be mapped
        mainSource.Should().NotContain("entity.Password");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // [MapProperty] with Default Value Tests
    // =========================================================================

    [Fact]
    public async Task MapProperty_WithDefault_UsesDefaultWhenNull()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Config
                         {
                             public int Id { get; set; }
                             public string? Theme { get; set; }
                             public int? PageSize { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Config>]
                         public partial record ConfigDto
                         {
                             public int Id { get; init; }
                             [MapProperty(Default = "light")]
                             public string Theme { get; init; } = "";
                             [MapProperty(Default = "10")]
                             public int PageSize { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ConfigDto.Mapping");
        // Should use null-coalescing with default
        mainSource.Should().Contain("??");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Collection Mapping Tests
    // =========================================================================

    [Fact]
    public async Task MapFrom_WithListOfPrimitives_CopiesCollection()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Tags
                         {
                             public int Id { get; set; }
                             public List<string> Values { get; set; } = new();
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Tags>]
                         public partial record TagsDto
                         {
                             public int Id { get; init; }
                             public List<string> Values { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "TagsDto.Mapping");
        // Should use .ToList() to copy, not .Select(...).ToList() for DTO mapping
        mainSource.Should().Contain(".ToList()");
        mainSource.Should().NotContain(".Select(");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // [MapProperty] with Format Tests
    // =========================================================================

    [Fact]
    public async Task MapProperty_WithFormat_DecimalToString_AppliesFormat()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Invoice
                         {
                             public int Id { get; set; }
                             public decimal Total { get; set; }
                             public decimal Tax { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Invoice>]
                         public partial record InvoiceDto
                         {
                             public int Id { get; init; }
                             [MapProperty(nameof(TestApp.Entities.Invoice.Total), Format = "F2")]
                             public string TotalText { get; init; } = "";
                             [MapProperty(nameof(TestApp.Entities.Invoice.Tax), Format = "C2")]
                             public string TaxText { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "InvoiceDto.Mapping");
        // Should use Format string with decimal -> string conversion
        mainSource.Should().Contain(".ToString(\"F2\"");
        mainSource.Should().Contain(".ToString(\"C2\"");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task MapProperty_WithFormat_IntToString_AppliesFormat()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public int Quantity { get; set; }
                             public int Price { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Product>]
                         public partial record ProductDto
                         {
                             public int Id { get; init; }
                             [MapProperty(nameof(TestApp.Entities.Product.Quantity), Format = "D5")]
                             public string QuantityText { get; init; } = "";
                             [MapProperty(nameof(TestApp.Entities.Product.Price), Format = "N0")]
                             public string PriceText { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        // Should use Format string with int -> string conversion
        mainSource.Should().Contain(".ToString(\"D5\"");
        mainSource.Should().Contain(".ToString(\"N0\"");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task MapProperty_WithFormat_DateTime_AppliesFormat()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public DateTime StartDate { get; set; }
                             public DateTime EndDate { get; set; }
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             [MapProperty(nameof(TestApp.Entities.Event.StartDate), Format = "yyyy-MM-dd")]
                             public string StartDateText { get; init; } = "";
                             [MapProperty(nameof(TestApp.Entities.Event.EndDate), Format = "dd/MM/yyyy HH:mm")]
                             public string EndDateText { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        // Should use Format string with DateTime -> string conversion
        mainSource.Should().Contain(".ToString(\"yyyy-MM-dd\"");
        mainSource.Should().Contain(".ToString(\"dd/MM/yyyy HH:mm\"");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // [MapConverter] Tests
    // =========================================================================

    [Fact]
    public async Task MapConverter_SimpleConversion_UsesConverter()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace TestApp
                     {
                         public class Entity
                         {
                             public int Id { get; set; }
                             public string Code { get; set; } = "";
                         }

                         public class UpperCaseConverter : IValueConverter<string, string>
                         {
                             public string Convert(string source) => source.ToUpperInvariant();
                             public string ConvertBack(string target) => target.ToLowerInvariant();
                         }

                         [MapFrom<Entity>]
                         public partial record EntityDto
                         {
                             public int Id { get; init; }
                             [MapConverter<UpperCaseConverter>]
                             public string Code { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EntityDto.Mapping");
        mainSource.Should().Contain("UpperCaseConverter");
        mainSource.Should().Contain(".Convert(");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public void MapTo_WithMapConverter_EmitsConvertBack()
    {
        // The write path (ToEntity) must honor [MapConverter] via ConvertBack,
        // matching the documented IValueConverter contract.
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace TestApp
                     {
                         public class Product
                         {
                             public string Sku { get; set; } = "";
                         }

                         public class SkuConverter : IValueConverter<string, string>
                         {
                             public string Convert(string source) => source.ToUpperInvariant();
                             public string ConvertBack(string target) => target.ToLowerInvariant();
                         }

                         [MapTo<Product>]
                         public partial record ProductDto
                         {
                             [MapConverter<SkuConverter>]
                             public string Sku { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        mainSource.Should().NotBeNull();
        // ToEntity must run the value through ConvertBack, not assign the DTO value directly.
        mainSource.Should().Contain("ConvertBack(this.Sku)");
        mainSource.Should().Contain("SkuConverter");
    }

    [Fact]
    public async Task MapProperty_WithMapConverter_BothApplied()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace TestApp
                     {
                         public class FileEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public long SizeBytes { get; set; }
                         }

                         public class BytesToHumanReadableConverter : IValueConverter<long, string>
                         {
                             public string Convert(long source)
                             {
                                 string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
                                 int index = 0;
                                 double size = source;
                                 while (size >= 1024 && index < suffixes.Length - 1)
                                 {
                                     size /= 1024;
                                     index++;
                                 }
                                 return $"{size:F2} {suffixes[index]}";
                             }
                             public long ConvertBack(string target) => 0;
                         }

                         [MapFrom<FileEntity>]
                         public partial record FileDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             // Different property name + custom converter
                             [MapProperty("SizeBytes")]
                             [MapConverter<BytesToHumanReadableConverter>]
                             public string SizeDisplay { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "FileDto.Mapping");
        // Should use the converter AND reference the correct source property
        mainSource.Should().Contain("BytesToHumanReadableConverter");
        mainSource.Should().Contain(".Convert(");
        mainSource.Should().Contain("entity.SizeBytes");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task MapProperty_WithMapConverter_DifferentTypes()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace TestApp
                     {
                         public class DateEntity
                         {
                             public int Id { get; set; }
                             public DateTime Timestamp { get; set; }
                         }

                         public class DateTimeToUnixConverter : IValueConverter<DateTime, long>
                         {
                             public long Convert(DateTime source) =>
                                 ((DateTimeOffset)source).ToUnixTimeSeconds();
                             public DateTime ConvertBack(long target) =>
                                 DateTimeOffset.FromUnixTimeSeconds(target).DateTime;
                         }

                         [MapFrom<DateEntity>]
                         public partial record DateDto
                         {
                             public int Id { get; init; }
                             [MapProperty("Timestamp")]
                             [MapConverter<DateTimeToUnixConverter>]
                             public long UnixTimestamp { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "DateDto.Mapping");
        mainSource.Should().Contain("DateTimeToUnixConverter");
        mainSource.Should().Contain(".Convert(");
        mainSource.Should().Contain("entity.Timestamp");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Internal Accessibility Tests
    // =========================================================================

    [Fact]
    public async Task MapFrom_InternalClass_GeneratesInternalMethods()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp.Entities
                     {
                         internal class InternalEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }
                     }

                     namespace TestApp.Dtos
                     {
                         [MapFrom<TestApp.Entities.InternalEntity>]
                         internal partial record InternalDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "InternalDto.Mapping");
        mainSource.Should().Contain("internal partial record InternalDto");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }
}
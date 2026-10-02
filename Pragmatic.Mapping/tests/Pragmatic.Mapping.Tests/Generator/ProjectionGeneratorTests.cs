using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for [GenerateProjection] attribute in MappingSourceGenerator.
///     Projections generate Expression&lt;Func&lt;TSource, TDto&gt;&gt; for EF Core.
/// </summary>
public class ProjectionGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // Basic Projection
    // =========================================================================

    [Fact]
    public async Task GenerateProjection_SimpleRecord_GeneratesProjectionProperty()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class User
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public string Email { get; set; } = "";
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.User>]
                         [GenerateProjection]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public string Email { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().Contain("public static Expression<Func<");
        mainSource.Should().Contain("Projection { get; }");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Projection with Flattening
    // =========================================================================

    [Fact]
    public async Task GenerateProjection_WithFlattening_GeneratesNestedAccess()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Address { public string City { get; set; } = ""; }
                         public class Customer
                         {
                             public int Id { get; set; }
                             public Address? Address { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Customer>]
                         [GenerateProjection]
                         public partial record CustomerDto
                         {
                             public int Id { get; init; }
                             public string? AddressCity { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CustomerDto.Mapping");
        mainSource.Should().Contain("entity.Address");
        mainSource.Should().Contain("City");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // A converter over the row is computed on the client after the read
    // =========================================================================

    [Fact]
    public async Task GenerateProjection_WithConverter_ComputesItAfterTheRead()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace Test.Converters
                     {
                         public class UpperConverter : IValueConverter<string, string>
                         {
                             public string Convert(string source) => source.ToUpper();
                             public string ConvertBack(string target) => target.ToLower();
                         }
                     }
                     namespace Test.Entities
                     {
                         public class Item { public int Id { get; set; } public string Code { get; set; } = ""; }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Item>]
                         [GenerateProjection]
                         public partial record ItemDto
                         {
                             public int Id { get; init; }
                             [MapConverter<Test.Converters.UpperConverter>]
                             public string Code { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ItemDto.Mapping");
        // Projection should exist
        mainSource.Should().Contain("Projection { get; }");
        mainSource.Should().Contain("Id = entity.Id");
        // Code has a converter: no SQL, so the client computes it in the top-level projection — through a
        // static method, since EF Core refuses an instance method on a constant there.
        mainSource.Should().Contain("Code = ConvertAfterTheRead_Code(entity.Code),");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nested DTO Projection
    // =========================================================================

    [Fact]
    public async Task GenerateProjection_WithNestedDto_InlinesPropertyMappings()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Address
                         {
                             public int Id { get; set; }
                             public string Street { get; set; } = "";
                             public string City { get; set; } = "";
                         }
                         public class Person
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Address? Address { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Address>]
                         [GenerateProjection]
                         public partial record AddressDto
                         {
                             public int Id { get; init; }
                             public string Street { get; init; } = "";
                             public string City { get; init; } = "";
                         }

                         [MapFrom<Test.Entities.Person>]
                         [GenerateProjection]
                         public partial record PersonDto
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

        var mainSource = GetGeneratedSource(result, "PersonDto.Mapping");
        // Projection should inline nested DTO properties
        mainSource.Should().Contain("Projection { get; }");
        // Should have inlined Address projection with property mappings
        mainSource.Should().Contain("new global::Test.Dtos.AddressDto");
        mainSource.Should().Contain("Street");
        mainSource.Should().Contain("City");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // No Projection without attribute
    // =========================================================================

    [Fact]
    public async Task MapFrom_WithoutGenerateProjection_DoesNotGenerateProjection()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Simple { public int Id { get; set; } }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Simple>]
                         public partial record SimpleDto { public int Id { get; init; } }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SimpleDto.Mapping");
        // Should NOT have Projection
        mainSource.Should().NotContain("Projection { get; }");
        mainSource.Should().NotContain("Expression<Func<");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }
}
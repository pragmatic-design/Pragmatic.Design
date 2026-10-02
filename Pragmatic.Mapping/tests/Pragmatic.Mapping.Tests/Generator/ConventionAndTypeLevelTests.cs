using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tier-3 parity features: class-level [MapConverter], snake_case naming fallback, general null
///     substitution (Default on nullable targets), and flattening/concatenation inlined into nested
///     projections (rather than dropped with PRAG0326).
/// </summary>
public class ConventionAndTypeLevelTests : MappingGeneratorTestBase
{
    [Fact]
    public void ClassLevelConverter_AppliesToMatchingTypePairs()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;
                     using Pragmatic.Mapping.Converters;

                     namespace TestApp
                     {
                         public struct Money { public decimal Amount { get; set; } }

                         public class MoneyToDecimalConverter : IValueConverter<Money, decimal>
                         {
                             public decimal Convert(Money source) => source.Amount;
                             public Money ConvertBack(decimal target) => new() { Amount = target };
                         }

                         public class Order
                         {
                             public Money Total { get; set; }
                             public Money Discount { get; set; }
                             public string Code { get; set; } = "";
                         }

                         [MapFrom<Order>]
                         [MapConverter<MoneyToDecimalConverter>]
                         public partial record OrderDto
                         {
                             public decimal Total { get; init; }     // Money → decimal via class-level converter
                             public decimal Discount { get; init; }  // same converter, no repetition
                             public string Code { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain("_converter_TestApp_MoneyToDecimalConverter.Convert(entity.Total)")
            .And.Contain("_converter_TestApp_MoneyToDecimalConverter.Convert(entity.Discount)")
            .And.Contain("Code = entity.Code", "non-matching types must not go through the converter");
    }

    [Fact]
    public void SnakeCaseSource_MatchesPascalCaseDto()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         // External-API style source with snake_case members
                         public class ApiUser
                         {
                             public string first_name { get; set; } = "";
                             public int login_count { get; set; }
                         }

                         [MapFrom<ApiUser>]
                         public partial record UserDto
                         {
                             public string FirstName { get; init; } = "";
                             public int LoginCount { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().Contain("FirstName = entity.first_name")
            .And.Contain("LoginCount = entity.login_count");
        HasDiagnostic(result, "PRAG0303").Should().BeFalse("both properties resolve by convention");
    }

    [Fact]
    public void Default_OnNullableTarget_ActsAsNullSubstitution()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source { public string? Nickname { get; set; } }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             // Nullable→nullable with Default: fallback value when source is null.
                             [MapProperty("Nickname", Default = "anonymous")]
                             public string? Nickname { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain("entity.Nickname ?? \"anonymous\"");
    }

    [Fact]
    public void NestedProjection_InlinesFlatteningAndConcatenation()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Address { public string City { get; set; } = ""; }
                         public class Person
                         {
                             public string FirstName { get; set; } = "";
                             public string LastName { get; set; } = "";
                             public Address Address { get; set; } = new();
                         }
                         public class Order { public Person Buyer { get; set; } = new(); }

                         [MapFrom<Person>]
                         public partial record PersonDto
                         {
                             [MapProperty("FirstName", "LastName")]
                             public string FullName { get; init; } = "";

                             [MapProperty("Address.City")]
                             public string City { get; init; } = "";
                         }

                         [MapFrom<Order>]
                         [GenerateProjection]
                         public partial record OrderDto
                         {
                             public PersonDto Buyer { get; init; } = null!;
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        // These members are inlined into the projection, not dropped with PRAG0326.
        mainSource.Should().Contain("FullName = entity.Buyer!.FirstName + \" \" + entity.Buyer!.LastName")
            .And.Contain("City = entity.Buyer!.Address.City");
        HasDiagnostic(result, "PRAG0326").Should().BeFalse(
            "flattening and concatenation now inline into the projection");
    }
}

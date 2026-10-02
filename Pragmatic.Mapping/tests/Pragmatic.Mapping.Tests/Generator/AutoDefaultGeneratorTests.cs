using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for auto-default behavior when mapping nullable to non-nullable types.
///     Auto-default automatically applies default values (0, false, "", etc.) when
///     no explicit [MapProperty(Default=...)] is specified.
/// </summary>
public class AutoDefaultGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // Numeric Types Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableInt_ToNonNullableInt_UsesGetValueOrDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int? Count { get; set; }
                             public int? Total { get; set; }
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Count { get; init; }
                             public int Total { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableDecimal_ToNonNullableDecimal_UsesGetValueOrDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public decimal? Total { get; set; }
                             public decimal? Discount { get; set; }
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public decimal Total { get; init; }
                             public decimal Discount { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Boolean Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableBool_ToNonNullableBool_UsesGetValueOrDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Settings
                         {
                             public bool? IsEnabled { get; set; }
                             public bool? IsVisible { get; set; }
                         }

                         [MapFrom<Settings>]
                         public partial record SettingsDto
                         {
                             public bool IsEnabled { get; init; }
                             public bool IsVisible { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SettingsDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // String Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableString_ToNonNullableString_UsesEmptyString()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class User
                         {
                             public string? Name { get; set; }
                             public string? Email { get; set; }
                         }

                         [MapFrom<User>]
                         public partial record UserDto
                         {
                             public string Name { get; init; } = "";
                             public string Email { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().Contain("?? \"\"");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // DateTime/Date/Time Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableDateTime_ToNonNullableDateTime_UsesGetValueOrDefault()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public DateTime? StartDate { get; set; }
                             public DateTime? EndDate { get; set; }
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public DateTime StartDate { get; init; }
                             public DateTime EndDate { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableDateOnly_ToNonNullableDateOnly_UsesGetValueOrDefault()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Booking
                         {
                             public DateOnly? CheckIn { get; set; }
                             public DateOnly? CheckOut { get; set; }
                         }

                         [MapFrom<Booking>]
                         public partial record BookingDto
                         {
                             public DateOnly CheckIn { get; init; }
                             public DateOnly CheckOut { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BookingDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Guid Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableGuid_ToNonNullableGuid_UsesGetValueOrDefault()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Document
                         {
                             public Guid? Id { get; set; }
                             public Guid? ParentId { get; set; }
                         }

                         [MapFrom<Document>]
                         public partial record DocumentDto
                         {
                             public Guid Id { get; init; }
                             public Guid ParentId { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "DocumentDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Enum Auto-Default
    // =========================================================================

    [Fact]
    public async Task NullableEnum_ToNonNullableEnum_UsesGetValueOrDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Status { Active, Inactive, Pending }

                         public class Account
                         {
                             public Status? Status { get; set; }
                             public Status? PreviousStatus { get; set; }
                         }

                         [MapFrom<Account>]
                         public partial record AccountDto
                         {
                             public Status Status { get; init; }
                             public Status PreviousStatus { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "AccountDto.Mapping");
        mainSource.Should().Contain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Projection Auto-Default (uses ?? operator instead of GetValueOrDefault)
    // =========================================================================

    [Fact]
    public async Task Projection_NullableInt_ToNonNullable_UsesNullCoalescing()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public int? Count { get; set; }
                             public decimal? Total { get; set; }
                         }

                         [MapFrom<Source>]
                         [GenerateProjection]
                         public partial record Target
                         {
                             public int Count { get; init; }
                             public decimal Total { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        // Projection uses ?? 0 instead of GetValueOrDefault for EF Core compatibility
        mainSource.Should().Contain("?? 0");
        mainSource.Should().Contain("?? 0m");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Projection_NullableString_ToNonNullable_UsesEmptyString()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class User
                         {
                             public string? Name { get; set; }
                         }

                         [MapFrom<User>]
                         [GenerateProjection]
                         public partial record UserDto
                         {
                             public string Name { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UserDto.Mapping");
        mainSource.Should().Contain("?? \"\"");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Projection_NullableGuid_ToNonNullable_UsesGuidEmpty()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Document
                         {
                             public Guid? Id { get; set; }
                         }

                         [MapFrom<Document>]
                         [GenerateProjection]
                         public partial record DocumentDto
                         {
                             public Guid Id { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "DocumentDto.Mapping");
        mainSource.Should().Contain("?? Guid.Empty");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Explicit Default Takes Precedence
    // =========================================================================

    [Fact]
    public async Task ExplicitDefault_TakesPrecedenceOverAutoDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Config
                         {
                             public int? Timeout { get; set; }
                             public string? Prefix { get; set; }
                         }

                         [MapFrom<Config>]
                         public partial record ConfigDto
                         {
                             [MapProperty(Default = "30")]
                             public int Timeout { get; init; }

                             [MapProperty(Default = "DEFAULT")]
                             public string Prefix { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ConfigDto.Mapping");
        // Should use explicit defaults, not auto-defaults
        mainSource.Should().Contain("?? 30");
        mainSource.Should().Contain("?? \"DEFAULT\"");
        mainSource.Should().NotContain("GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }
}
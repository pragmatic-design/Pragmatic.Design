using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for automatic type conversions in mapping (string to/from numeric, date, enum, etc.).
/// </summary>
public class AutomaticConversionGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // String to Numeric Conversions
    // =========================================================================

    [Fact]
    public async Task StringToInt_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Source
                         {
                             public string Id { get; set; } = "";
                             public string Count { get; set; } = "";
                         }

                         [MapFrom<Source>]
                         public partial record Target
                         {
                             public int Id { get; init; }
                             public int Count { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "Target.Mapping");
        mainSource.Should().Contain("int.Parse");
        mainSource.Should().Contain("InvariantCulture");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToDecimal_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public string Total { get; set; } = "";
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public decimal Total { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain("decimal.Parse");
        mainSource.Should().Contain("InvariantCulture");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // String to DateTime/DateOnly/TimeOnly Conversions
    // =========================================================================

    [Fact]
    public async Task StringToDateTime_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public string StartDate { get; set; } = "";
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public DateTime StartDate { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain("DateTime.Parse");
        mainSource.Should().Contain("InvariantCulture");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToDateOnly_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public string EventDate { get; set; } = "";
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public DateOnly EventDate { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain("DateOnly.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // String to Enum Conversions
    // =========================================================================

    [Fact]
    public async Task StringToEnum_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Priority { Low, Medium, High }

                         public class Task
                         {
                             public int Id { get; set; }
                             public string Priority { get; set; } = "";
                         }

                         [MapFrom<Task>]
                         public partial record TaskDto
                         {
                             public int Id { get; init; }
                             public Priority Priority { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "TaskDto.Mapping");
        mainSource.Should().Contain("Enum.Parse");
        mainSource.Should().Contain("ignoreCase: true");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // String to Guid Conversions
    // =========================================================================

    [Fact]
    public async Task StringToGuid_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Document
                         {
                             public string Id { get; set; } = "";
                             public string ParentId { get; set; } = "";
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
        mainSource.Should().Contain("Guid.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // DateTime to DateOnly/TimeOnly Conversions
    // =========================================================================

    [Fact]
    public async Task DateTimeToDateOnly_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Appointment
                         {
                             public int Id { get; set; }
                             public DateTime ScheduledAt { get; set; }
                         }

                         [MapFrom<Appointment>]
                         public partial record AppointmentDto
                         {
                             public int Id { get; init; }
                             public DateOnly ScheduledAt { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "AppointmentDto.Mapping");
        mainSource.Should().Contain("DateOnly.FromDateTime");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task DateTimeToTimeOnly_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Meeting
                         {
                             public int Id { get; set; }
                             public DateTime StartTime { get; set; }
                         }

                         [MapFrom<Meeting>]
                         public partial record MeetingDto
                         {
                             public int Id { get; init; }
                             public TimeOnly StartTime { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeetingDto.Mapping");
        mainSource.Should().Contain("TimeOnly.FromDateTime");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // X to String Conversions
    // =========================================================================

    [Fact]
    public async Task IntToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public int Quantity { get; set; }
                         }

                         [MapFrom<Product>]
                         public partial record ProductDto
                         {
                             public string Id { get; init; } = "";
                             public string Quantity { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        mainSource.Should().Contain(".ToString(");
        mainSource.Should().Contain("InvariantCulture");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task EnumToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Status { Active, Inactive }

                         public class Account
                         {
                             public int Id { get; set; }
                             public Status Status { get; set; }
                         }

                         [MapFrom<Account>]
                         public partial record AccountDto
                         {
                             public int Id { get; init; }
                             public string Status { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "AccountDto.Mapping");
        mainSource.Should().Contain(".ToString()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task GuidToString_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Session
                         {
                             public Guid Id { get; set; }
                             public Guid Token { get; set; }
                         }

                         [MapFrom<Session>]
                         public partial record SessionDto
                         {
                             public string Id { get; init; } = "";
                             public string Token { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SessionDto.Mapping");
        mainSource.Should().Contain(".ToString()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // DateOnly to DateTime Conversions
    // =========================================================================

    [Fact]
    public async Task DateOnlyToDateTime_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Holiday
                         {
                             public int Id { get; set; }
                             public DateOnly Date { get; set; }
                         }

                         [MapFrom<Holiday>]
                         public partial record HolidayDto
                         {
                             public int Id { get; init; }
                             public DateTime Date { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "HolidayDto.Mapping");
        mainSource.Should().Contain(".ToDateTime(");
        mainSource.Should().Contain("TimeOnly.MinValue");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }
}
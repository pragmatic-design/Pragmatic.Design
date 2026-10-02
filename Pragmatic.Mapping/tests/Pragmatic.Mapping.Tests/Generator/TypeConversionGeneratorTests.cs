using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for type conversions in mapping.
/// </summary>
public class TypeConversionGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // Enum Mapping Tests
    // =========================================================================

    [Fact]
    public async Task Enum_SameType_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum OrderStatus { Pending, Completed, Cancelled }

                         public class Order
                         {
                             public int Id { get; set; }
                             public OrderStatus Status { get; set; }
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public OrderStatus Status { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain("entity.Status");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableEnum_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Priority { Low, Medium, High }

                         public class Task
                         {
                             public int Id { get; set; }
                             public Priority? Priority { get; set; }
                         }

                         [MapFrom<Task>]
                         public partial record TaskDto
                         {
                             public int Id { get; init; }
                             public Priority? Priority { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // DateOnly / TimeOnly Tests
    // =========================================================================

    [Fact]
    public async Task DateOnly_MapsDirectly()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public DateOnly Date { get; set; }
                             public TimeOnly StartTime { get; set; }
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public DateOnly Date { get; init; }
                             public TimeOnly StartTime { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableDateOnly_MapsDirectly()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Appointment
                         {
                             public int Id { get; set; }
                             public DateOnly? ScheduledDate { get; set; }
                             public TimeOnly? ScheduledTime { get; set; }
                         }

                         [MapFrom<Appointment>]
                         public partial record AppointmentDto
                         {
                             public int Id { get; init; }
                             public DateOnly? ScheduledDate { get; init; }
                             public TimeOnly? ScheduledTime { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Guid Tests
    // =========================================================================

    [Fact]
    public async Task NullableGuid_MapsDirectly()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Document
                         {
                             public Guid Id { get; set; }
                             public Guid? ParentId { get; set; }
                         }

                         [MapFrom<Document>]
                         public partial record DocumentDto
                         {
                             public Guid Id { get; init; }
                             public Guid? ParentId { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Implicit Widening Conversion Tests
    // =========================================================================

    [Fact]
    public async Task IntToLong_ImplicitConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Counter
                         {
                             public int Id { get; set; }
                             public int Count { get; set; }
                         }

                         [MapFrom<Counter>]
                         public partial record CounterDto
                         {
                             public int Id { get; init; }
                             public long Count { get; init; }  // int -> long widening
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task IntToDecimal_ImplicitConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Price
                         {
                             public int Id { get; set; }
                             public int Amount { get; set; }
                         }

                         [MapFrom<Price>]
                         public partial record PriceDto
                         {
                             public int Id { get; init; }
                             public decimal Amount { get; init; }  // int -> decimal widening
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Nullable to Non-Nullable Without Default (Should Warn or Error)
    // =========================================================================

    [Fact]
    public async Task NullableIntToInt_WithoutDefault_ShouldCompile()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Item
                         {
                             public int Id { get; set; }
                             public int? Quantity { get; set; }
                         }

                         [MapFrom<Item>]
                         public partial record ItemDto
                         {
                             public int Id { get; init; }
                             public int Quantity { get; init; }  // int? -> int (potential null!)
                         }
                     }
                     """;

        var result = RunGenerator(source);

        // This should compile but might have warnings
        // The actual behavior depends on nullable analysis
        var errors = GetCompilationErrors(result);
        var warnings = GetCompilationWarnings(result);

        // Document actual behavior
        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(new
        {
            Sources = sources,
            Errors = errors.Select(e => e.ToString()),
            Warnings = warnings.Select(w => w.ToString())
        });
    }

    [Fact]
    public async Task NullableDateTimeToDateTime_WithoutDefault_ShouldCompile()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public DateTime? StartTime { get; set; }
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public DateTime StartTime { get; init; }  // DateTime? -> DateTime
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain(".GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableGuidToGuid_WithoutDefault_ShouldCompile()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Session
                         {
                             public int Id { get; set; }
                             public Guid? Token { get; set; }
                         }

                         [MapFrom<Session>]
                         public partial record SessionDto
                         {
                             public int Id { get; init; }
                             public Guid Token { get; init; }  // Guid? -> Guid
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SessionDto.Mapping");
        mainSource.Should().Contain(".GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableDecimalToDecimal_WithoutDefault_ShouldCompile()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Price
                         {
                             public int Id { get; set; }
                             public decimal? Amount { get; set; }
                         }

                         [MapFrom<Price>]
                         public partial record PriceDto
                         {
                             public int Id { get; init; }
                             public decimal Amount { get; init; }  // decimal? -> decimal
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PriceDto.Mapping");
        mainSource.Should().Contain(".GetValueOrDefault()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Collection Type Conversion Tests
    // =========================================================================

    [Fact]
    public async Task ListToArray_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public List<string> Tags { get; set; } = new();
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string[] Tags { get; init; } = [];  // List<T> -> T[]
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain(".ToArray()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task ArrayToList_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public string[] Tags { get; set; } = [];
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public List<string> Tags { get; init; } = new();  // T[] -> List<T>
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain(".ToList()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task IEnumerableToList_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public IEnumerable<int> Values { get; set; } = [];
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public List<int> Values { get; init; } = new();  // IEnumerable<T> -> List<T>
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain(".ToList()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task ListToIEnumerable_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public List<int> Values { get; set; } = new();
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public IEnumerable<int> Values { get; init; } = [];  // List<T> -> IEnumerable<T>
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task ListToHashSet_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public List<string> Tags { get; set; } = new();
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public HashSet<string> Tags { get; init; } = new();  // List<T> -> HashSet<T>
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain(".ToHashSet()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task ArrayToIReadOnlyList_Conversion()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Order
                         {
                             public int Id { get; set; }
                             public int[] Values { get; set; } = [];
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public IReadOnlyList<int> Values { get; init; } = [];  // T[] -> IReadOnlyList<T>
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: ToString Conversions
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
                             public int Id { get; init; }
                             public string Quantity { get; init; } = "";  // int -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        mainSource.Should().Contain(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");

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
                             public string Id { get; init; } = "";  // Guid -> string
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

    [Fact]
    public async Task EnumToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum OrderStatus { Pending, Completed, Cancelled }

                         public class Order
                         {
                             public int Id { get; set; }
                             public OrderStatus Status { get; set; }
                         }

                         [MapFrom<Order>]
                         public partial record OrderDto
                         {
                             public int Id { get; init; }
                             public string Status { get; init; } = "";  // enum -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderDto.Mapping");
        mainSource.Should().Contain(".ToString()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task DecimalToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Invoice
                         {
                             public int Id { get; set; }
                             public decimal Total { get; set; }
                         }

                         [MapFrom<Invoice>]
                         public partial record InvoiceDto
                         {
                             public int Id { get; init; }
                             public string Total { get; init; } = "";  // decimal -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "InvoiceDto.Mapping");
        mainSource.Should().Contain(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task BoolToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Setting
                         {
                             public int Id { get; set; }
                             public bool IsEnabled { get; set; }
                         }

                         [MapFrom<Setting>]
                         public partial record SettingDto
                         {
                             public int Id { get; init; }
                             public string IsEnabled { get; init; } = "";  // bool -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SettingDto.Mapping");
        mainSource.Should().Contain(".ToString()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task DateTimeToString_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public DateTime CreatedAt { get; set; }
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public string CreatedAt { get; init; } = "";  // DateTime -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain(".ToString(\"o\""); // ISO 8601 format

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: Parse Conversions (string -> X)
    // =========================================================================

    [Fact]
    public async Task StringToInt_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class ProductDto
                         {
                             public int Id { get; set; }
                             public string Quantity { get; set; } = "";
                         }

                         public class Product
                         {
                             public int Id { get; set; }
                             public int Quantity { get; set; }  // string -> int
                         }

                         [MapFrom<ProductDto>]
                         public partial record ProductEntity
                         {
                             public int Id { get; init; }
                             public int Quantity { get; init; }  // string -> int
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductEntity.Mapping");
        mainSource.Should().Contain("int.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToGuid_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class SessionDto
                         {
                             public string Id { get; set; } = "";
                             public string Token { get; set; } = "";
                         }

                         [MapFrom<SessionDto>]
                         public partial record SessionEntity
                         {
                             public Guid Id { get; init; }  // string -> Guid
                             public Guid Token { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SessionEntity.Mapping");
        mainSource.Should().Contain("global::System.Guid.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToEnum_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum OrderStatus { Pending, Completed, Cancelled }

                         public class OrderDto
                         {
                             public int Id { get; set; }
                             public string Status { get; set; } = "";
                         }

                         [MapFrom<OrderDto>]
                         public partial record OrderEntity
                         {
                             public int Id { get; init; }
                             public OrderStatus Status { get; init; }  // string -> enum
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "OrderEntity.Mapping");
        mainSource.Should().Contain("global::System.Enum.Parse<");

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
                         public class InvoiceDto
                         {
                             public int Id { get; set; }
                             public string Total { get; set; } = "";
                         }

                         [MapFrom<InvoiceDto>]
                         public partial record InvoiceEntity
                         {
                             public int Id { get; init; }
                             public decimal Total { get; init; }  // string -> decimal
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "InvoiceEntity.Mapping");
        mainSource.Should().Contain("decimal.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToBool_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class SettingDto
                         {
                             public int Id { get; set; }
                             public string IsEnabled { get; set; } = "";
                         }

                         [MapFrom<SettingDto>]
                         public partial record SettingEntity
                         {
                             public int Id { get; init; }
                             public bool IsEnabled { get; init; }  // string -> bool
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "SettingEntity.Mapping");
        mainSource.Should().Contain("bool.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToDateTime_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class EventDto
                         {
                             public int Id { get; set; }
                             public string CreatedAt { get; set; } = "";
                         }

                         [MapFrom<EventDto>]
                         public partial record EventEntity
                         {
                             public int Id { get; init; }
                             public DateTime CreatedAt { get; init; }  // string -> DateTime
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventEntity.Mapping");
        mainSource.Should().Contain("global::System.DateTime.Parse");

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
                         public class BookingDto
                         {
                             public int Id { get; set; }
                             public string CheckIn { get; set; } = "";
                         }

                         [MapFrom<BookingDto>]
                         public partial record BookingEntity
                         {
                             public int Id { get; init; }
                             public DateOnly CheckIn { get; init; }  // string -> DateOnly
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BookingEntity.Mapping");
        mainSource.Should().Contain("global::System.DateOnly.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToTimeOnly_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class ScheduleDto
                         {
                             public int Id { get; set; }
                             public string StartTime { get; set; } = "";
                         }

                         [MapFrom<ScheduleDto>]
                         public partial record ScheduleEntity
                         {
                             public int Id { get; init; }
                             public TimeOnly StartTime { get; init; }  // string -> TimeOnly
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ScheduleEntity.Mapping");
        mainSource.Should().Contain("global::System.TimeOnly.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P2: DateTime <-> DateOnly/TimeOnly Conversions
    // =========================================================================

    [Fact]
    public async Task DateTimeToDateOnly_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Event
                         {
                             public int Id { get; set; }
                             public DateTime OccurredAt { get; set; }
                         }

                         [MapFrom<Event>]
                         public partial record EventDto
                         {
                             public int Id { get; init; }
                             public DateOnly OccurredAt { get; init; }  // DateTime -> DateOnly
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "EventDto.Mapping");
        mainSource.Should().Contain("global::System.DateOnly.FromDateTime");

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
                         public class Alarm
                         {
                             public int Id { get; set; }
                             public DateTime TriggerAt { get; set; }
                         }

                         [MapFrom<Alarm>]
                         public partial record AlarmDto
                         {
                             public int Id { get; init; }
                             public TimeOnly TriggerAt { get; init; }  // DateTime -> TimeOnly
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "AlarmDto.Mapping");
        mainSource.Should().Contain("global::System.TimeOnly.FromDateTime");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task DateOnlyToDateTime_AutomaticConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class BookingDto
                         {
                             public int Id { get; set; }
                             public DateOnly CheckIn { get; set; }
                         }

                         [MapFrom<BookingDto>]
                         public partial record BookingEntity
                         {
                             public int Id { get; init; }
                             public DateTime CheckIn { get; init; }  // DateOnly -> DateTime
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BookingEntity.Mapping");
        mainSource.Should().Contain(".ToDateTime(global::System.TimeOnly.MinValue)");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1/P2: Nullable Source Conversions
    // =========================================================================

    [Fact]
    public async Task NullableIntToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Product
                         {
                             public int Id { get; set; }
                             public int? Quantity { get; set; }
                         }

                         [MapFrom<Product>]
                         public partial record ProductDto
                         {
                             public int Id { get; init; }
                             public string Quantity { get; init; } = "";  // int? -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductDto.Mapping");
        mainSource.Should().Contain("?.ToString");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableEnumToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public enum Status { Active, Inactive }

                         public class Item
                         {
                             public int Id { get; set; }
                             public Status? Status { get; set; }
                         }

                         [MapFrom<Item>]
                         public partial record ItemDto
                         {
                             public int Id { get; init; }
                             public string? Status { get; init; }  // enum? -> string?
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ItemDto.Mapping");
        mainSource.Should().Contain("?.ToString()");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableStringToInt_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class ProductDto
                         {
                             public int Id { get; set; }
                             public string? Quantity { get; set; }
                         }

                         [MapFrom<ProductDto>]
                         public partial record ProductEntity
                         {
                             public int Id { get; init; }
                             public int Quantity { get; init; }  // string? -> int
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductEntity.Mapping");
        mainSource.Should().Contain("is { } __str"); // Pattern matching for null check
        mainSource.Should().Contain("int.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P3: Dictionary with DTO Values
    // =========================================================================

    [Fact]
    public async Task DictionaryWithDtoValue_MapsEachValue()
    {
        var source = """
                     using System.Collections.Generic;
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Category
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         [MapFrom<Category>]
                         public partial record CategoryDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                         }

                         public class Store
                         {
                             public int Id { get; set; }
                             public Dictionary<string, Category> Categories { get; set; } = new();
                         }

                         [MapFrom<Store>]
                         public partial record StoreDto
                         {
                             public int Id { get; init; }
                             public Dictionary<string, CategoryDto> Categories { get; init; } = new();
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "StoreDto.Mapping");
        mainSource.Should().Contain(".ToDictionary(");
        mainSource.Should().Contain("CategoryDto.FromEntity");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P3: Inheritance Mapping (Base -> Derived)
    // =========================================================================

    [Fact]
    public async Task InheritedProperties_MapCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class BaseEntity
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                         }

                         public class DerivedEntity : BaseEntity
                         {
                             public string Description { get; set; } = "";
                         }

                         [MapFrom<DerivedEntity>]
                         public partial record DerivedDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public string Description { get; init; } = "";
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "DerivedDto.Mapping");
        mainSource.Should().Contain("entity.Id");
        mainSource.Should().Contain("entity.Name");
        mainSource.Should().Contain("entity.Description");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: double/float Primitive Types
    // =========================================================================

    [Fact]
    public async Task Double_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Measurement
                         {
                             public int Id { get; set; }
                             public double Value { get; set; }
                             public double Precision { get; set; }
                         }

                         [MapFrom<Measurement>]
                         public partial record MeasurementDto
                         {
                             public int Id { get; init; }
                             public double Value { get; init; }
                             public double Precision { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementDto.Mapping");
        mainSource.Should().Contain("entity.Value");
        mainSource.Should().Contain("entity.Precision");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Float_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Coordinate
                         {
                             public int Id { get; set; }
                             public float Latitude { get; set; }
                             public float Longitude { get; set; }
                         }

                         [MapFrom<Coordinate>]
                         public partial record CoordinateDto
                         {
                             public int Id { get; init; }
                             public float Latitude { get; init; }
                             public float Longitude { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CoordinateDto.Mapping");
        mainSource.Should().Contain("entity.Latitude");
        mainSource.Should().Contain("entity.Longitude");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableDouble_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Sample
                         {
                             public int Id { get; set; }
                             public double? Temperature { get; set; }
                         }

                         [MapFrom<Sample>]
                         public partial record SampleDto
                         {
                             public int Id { get; init; }
                             public double? Temperature { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableFloat_MapsDirectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Sensor
                         {
                             public int Id { get; set; }
                             public float? Reading { get; set; }
                         }

                         [MapFrom<Sensor>]
                         public partial record SensorDto
                         {
                             public int Id { get; init; }
                             public float? Reading { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Double_MapTo_WorksCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Measurement
                         {
                             public int Id { get; set; }
                             public double Value { get; set; }
                         }

                         [MapTo<Measurement>]
                         public partial record MeasurementDto
                         {
                             public int Id { get; init; }
                             public double Value { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementDto.Mapping");
        mainSource.Should().Contain("ToEntity");
        mainSource.Should().Contain("Value = this.Value");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Float_MapTo_WorksCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Coordinate
                         {
                             public int Id { get; set; }
                             public float Latitude { get; set; }
                         }

                         [MapTo<Coordinate>]
                         public partial record CoordinateDto
                         {
                             public int Id { get; init; }
                             public float Latitude { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CoordinateDto.Mapping");
        mainSource.Should().Contain("ToEntity");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task Double_Projection_WorksCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Measurement
                         {
                             public int Id { get; set; }
                             public double Value { get; set; }
                         }

                         [MapFrom<Measurement>]
                         [GenerateProjection]
                         public partial record MeasurementDto
                         {
                             public int Id { get; init; }
                             public double Value { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementDto.Mapping");
        mainSource.Should().Contain("Projection");
        mainSource.Should().Contain("Value = entity.Value");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: long -> string and string -> long Conversions
    // =========================================================================

    [Fact]
    public async Task LongToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class BigData
                         {
                             public int Id { get; set; }
                             public long TotalBytes { get; set; }
                         }

                         [MapFrom<BigData>]
                         public partial record BigDataDto
                         {
                             public int Id { get; init; }
                             public string TotalBytes { get; init; } = "";  // long -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BigDataDto.Mapping");
        mainSource.Should().Contain(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToLong_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class BigDataDto
                         {
                             public int Id { get; set; }
                             public string TotalBytes { get; set; } = "";
                         }

                         [MapFrom<BigDataDto>]
                         public partial record BigDataEntity
                         {
                             public int Id { get; init; }
                             public long TotalBytes { get; init; }  // string -> long
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BigDataEntity.Mapping");
        mainSource.Should().Contain("long.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableLongToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Counter
                         {
                             public int Id { get; set; }
                             public long? Count { get; set; }
                         }

                         [MapFrom<Counter>]
                         public partial record CounterDto
                         {
                             public int Id { get; init; }
                             public string? Count { get; init; }  // long? -> string?
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CounterDto.Mapping");
        mainSource.Should().Contain("?.ToString");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task NullableStringToLong_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class CounterDto
                         {
                             public int Id { get; set; }
                             public string? Count { get; set; }
                         }

                         [MapFrom<CounterDto>]
                         public partial record CounterEntity
                         {
                             public int Id { get; init; }
                             public long Count { get; init; }  // string? -> long
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CounterEntity.Mapping");
        mainSource.Should().Contain("is { } __str"); // Pattern matching for null check
        mainSource.Should().Contain("long.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: double -> string and string -> double Conversions
    // =========================================================================

    [Fact]
    public async Task DoubleToString_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Measurement
                         {
                             public int Id { get; set; }
                             public double Value { get; set; }
                         }

                         [MapFrom<Measurement>]
                         public partial record MeasurementStringDto
                         {
                             public int Id { get; init; }
                             public string Value { get; init; } = "";  // double -> string
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementStringDto.Mapping");
        mainSource.Should().Contain(".ToString(global::System.Globalization.CultureInfo.InvariantCulture)");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task StringToDouble_AutomaticConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class MeasurementDto
                         {
                             public int Id { get; set; }
                             public string Value { get; set; } = "";
                         }

                         [MapFrom<MeasurementDto>]
                         public partial record MeasurementEntity
                         {
                             public int Id { get; init; }
                             public double Value { get; init; }  // string -> double
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementEntity.Mapping");
        mainSource.Should().Contain("double.Parse");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // P1: Numeric Widening Conversions
    // =========================================================================

    [Fact]
    public async Task IntToDouble_ImplicitConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Stats
                         {
                             public int Id { get; set; }
                             public int Count { get; set; }
                         }

                         [MapFrom<Stats>]
                         public partial record StatsDto
                         {
                             public int Id { get; init; }
                             public double Count { get; init; }  // int -> double widening
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "StatsDto.Mapping");
        mainSource.Should().Contain("entity.Count"); // implicit conversion

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task LongToDecimal_ImplicitConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class BigStats
                         {
                             public int Id { get; set; }
                             public long Total { get; set; }
                         }

                         [MapFrom<BigStats>]
                         public partial record BigStatsDto
                         {
                             public int Id { get; init; }
                             public decimal Total { get; init; }  // long -> decimal widening
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "BigStatsDto.Mapping");
        mainSource.Should().Contain("entity.Total"); // implicit conversion

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    [Fact]
    public async Task FloatToDouble_ImplicitConversion()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class Precision
                         {
                             public int Id { get; set; }
                             public float Value { get; set; }
                         }

                         [MapFrom<Precision>]
                         public partial record PrecisionDto
                         {
                             public int Id { get; init; }
                             public double Value { get; init; }  // float -> double widening
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PrecisionDto.Mapping");
        mainSource.Should().Contain("entity.Value"); // implicit conversion

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }

    // =========================================================================
    // Circular Reference Test
    // =========================================================================

    [Fact]
    public async Task CircularReference_GeneratesVisitedTracking()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace TestApp
                     {
                         public class User
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public Team? Team { get; set; }
                         }

                         public class Team
                         {
                             public int Id { get; set; }
                             public string Name { get; set; } = "";
                             public User? Leader { get; set; }  // Circular: Team -> User -> Team
                         }

                         [MapFrom<User>]
                         public partial record UserDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public TeamDto? Team { get; init; }
                         }

                         [MapFrom<Team>]
                         public partial record TeamDto
                         {
                             public int Id { get; init; }
                             public string Name { get; init; } = "";
                             public UserDto? Leader { get; init; }  // Circular reference!
                         }
                     }
                     """;

        var result = RunGenerator(source);

        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var userDtoSource = GetGeneratedSource(result, "UserDto.Mapping");
        userDtoSource.Should().Contain("HashSet<object>");
        userDtoSource.Should().Contain("visited");
        userDtoSource.Should().Contain("!visited.Add(entity)");

        var sources = GetGeneratedSourcesAsDictionary(result);
        await Verify(sources);
    }
}
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Mapping.Tests.Generator;

/// <summary>
///     Tests for struct (non-record) mapping in MappingSourceGenerator.
///     <para>
///         Structs have different semantics than classes:
///         - Value type (copy on assignment)
///         - Cannot be null (no null check needed for source)
///         - Default constructor always available
///     </para>
///     <para>
///         Matrix of combinations tested:
///         <list type="table">
///             <listheader>
///                 <term>Struct Kind</term>
///                 <description>MapFrom | MapTo | Projection</description>
///             </listheader>
///             <item>
///                 <term>struct (DTO)</term><description>MapFrom, MapTo, Projection</description>
///             </item>
///             <item>
///                 <term>struct (Source)</term><description>No null check</description>
///             </item>
///             <item>
///                 <term>readonly struct</term><description>MapFrom, MapTo</description>
///             </item>
///             <item>
///                 <term>Nested struct</term><description>Value copy</description>
///             </item>
///         </list>
///     </para>
/// </summary>
public class StructMappingGeneratorTests : MappingGeneratorTestBase
{
    // =========================================================================
    // Struct DTO - MapFrom
    // =========================================================================

    [Fact]
    public async Task Struct_MapFrom_GeneratesFromEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Coordinate
                         {
                             public double Latitude { get; set; }
                             public double Longitude { get; set; }
                             public double? Altitude { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Coordinate>]
                         public partial struct CoordinateDto
                         {
                             public double Latitude { get; set; }
                             public double Longitude { get; set; }
                             public double? Altitude { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "CoordinateDto.Mapping");
        mainSource.Should().Contain("public partial struct CoordinateDto");
        mainSource.Should().Contain("FromEntity");
        mainSource.Should().Contain("Latitude = entity.Latitude");
        mainSource.Should().Contain("Longitude = entity.Longitude");
        mainSource.Should().Contain("Altitude = entity.Altitude");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Struct_MapFrom_WithMultipleProperties_GeneratesAllMappings()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Measurement
                         {
                             public int Id { get; set; }
                             public decimal Value { get; set; }
                             public string Unit { get; set; } = "";
                             public DateTime Timestamp { get; set; }
                             public bool IsValid { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Measurement>]
                         public partial struct MeasurementDto
                         {
                             public int Id { get; set; }
                             public decimal Value { get; set; }
                             public string Unit { get; set; }
                             public DateTime Timestamp { get; set; }
                             public bool IsValid { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MeasurementDto.Mapping");
        mainSource.Should().Contain("public partial struct MeasurementDto");
        mainSource.Should().Contain("Id = entity.Id");
        mainSource.Should().Contain("Value = entity.Value");
        mainSource.Should().Contain("Unit = entity.Unit");
        mainSource.Should().Contain("Timestamp = entity.Timestamp");
        mainSource.Should().Contain("IsValid = entity.IsValid");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct DTO - MapTo
    // =========================================================================

    [Fact]
    public async Task Struct_MapTo_GeneratesToEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Vector
                         {
                             public float X { get; set; }
                             public float Y { get; set; }
                             public float Z { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Vector>]
                         public partial struct VectorDto
                         {
                             public float X { get; set; }
                             public float Y { get; set; }
                             public float Z { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "VectorDto.Mapping");
        mainSource.Should().Contain("public partial struct VectorDto");
        mainSource.Should().Contain("ToEntity");
        mainSource.Should().Contain("X = this.X");
        mainSource.Should().Contain("Y = this.Y");
        mainSource.Should().Contain("Z = this.Z");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Struct_MapTo_WithSettings_GeneratesToEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Settings
                         {
                             public int Id { get; set; }
                             public int Timeout { get; set; }
                             public int MaxRetries { get; set; }
                             public bool EnableLogging { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Settings>]
                         public partial struct UpdateSettingsDto
                         {
                             public int Timeout { get; set; }
                             public int MaxRetries { get; set; }
                             public bool EnableLogging { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "UpdateSettingsDto.Mapping");
        mainSource.Should().Contain("public partial struct UpdateSettingsDto");
        mainSource.Should().Contain("ToEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct DTO - Projection
    // =========================================================================

    [Fact]
    public async Task Struct_Projection_GeneratesExpressionProperty()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Price
                         {
                             public decimal Amount { get; set; }
                             public string Currency { get; set; } = "";
                             public decimal? Discount { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Price>]
                         [GenerateProjection]
                         public partial struct PriceDto
                         {
                             public decimal Amount { get; set; }
                             public string Currency { get; set; }
                             public decimal? Discount { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PriceDto.Mapping");
        mainSource.Should().Contain("public partial struct PriceDto");
        mainSource.Should().Contain("Projection { get; }");
        mainSource.Should().Contain("Expression<Func<");
        mainSource.Should().Contain("Amount = entity.Amount");
        mainSource.Should().Contain("Currency = entity.Currency");
        mainSource.Should().Contain("Discount = entity.Discount");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task Struct_Projection_WithFlattening_GeneratesCorrectExpression()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Money { public string Code { get; set; } = ""; }
                         public class ProductPrice
                         {
                             public decimal Amount { get; set; }
                             public Money? Currency { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.ProductPrice>]
                         [GenerateProjection]
                         public partial struct ProductPriceDto
                         {
                             public decimal Amount { get; set; }
                             public string? CurrencyCode { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ProductPriceDto.Mapping");
        mainSource.Should().Contain("public partial struct ProductPriceDto");
        mainSource.Should().Contain("Projection { get; }");
        // Flattening should access Currency.Code
        mainSource.Should().Contain("Currency");
        mainSource.Should().Contain("Code");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct DTO - Bidirectional (MapFrom + MapTo)
    // =========================================================================

    [Fact]
    public async Task Struct_Bidirectional_GeneratesBothMethods()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Dimension
                         {
                             public int Width { get; set; }
                             public int Height { get; set; }
                             public int Depth { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Dimension>]
                         [MapTo<Test.Entities.Dimension>]
                         public partial struct DimensionDto
                         {
                             public int Width { get; set; }
                             public int Height { get; set; }
                             public int Depth { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "DimensionDto.Mapping");
        mainSource.Should().Contain("public partial struct DimensionDto");
        mainSource.Should().Contain("FromEntity");
        mainSource.Should().Contain("ToEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct Source - No Null Check
    // =========================================================================

    [Fact]
    public async Task StructSource_MapFrom_SkipsNullCheck()
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
                         public partial record PointDto
                         {
                             public int X { get; init; }
                             public int Y { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PointDto.Mapping");
        // Struct source should NOT have null check
        mainSource.Should().NotContain("ThrowIfNull");
        mainSource.Should().NotContain("ArgumentNullException");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task ClassSource_MapFrom_IncludesNullCheck()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Point
                         {
                             public int X { get; set; }
                             public int Y { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Point>]
                         public partial record PointDto
                         {
                             public int X { get; init; }
                             public int Y { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "PointDto.Mapping");
        // Class source should have null check
        mainSource.Should().Contain("ThrowIfNull");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Readonly Struct
    // =========================================================================

    [Fact]
    public async Task ReadonlyStruct_MapFrom_GeneratesCorrectly()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Color
                         {
                             public byte R { get; set; }
                             public byte G { get; set; }
                             public byte B { get; set; }
                             public byte A { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Color>]
                         public readonly partial struct ColorDto
                         {
                             public byte R { get; init; }
                             public byte G { get; init; }
                             public byte B { get; init; }
                             public byte A { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ColorDto.Mapping");
        // Note: Generator outputs "public partial struct" (readonly is preserved in user code)
        mainSource.Should().Contain("public partial struct ColorDto");
        mainSource.Should().Contain("FromEntity");
        mainSource.Should().Contain("R = entity.R");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    [Fact]
    public async Task ReadonlyStruct_MapTo_GeneratesToEntity()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Range
                         {
                             public int Start { get; set; }
                             public int End { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapTo<Test.Entities.Range>]
                         public readonly partial struct RangeDto
                         {
                             public int Start { get; init; }
                             public int End { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "RangeDto.Mapping");
        // Note: Generator outputs "public partial struct" (readonly is preserved in user code)
        mainSource.Should().Contain("public partial struct RangeDto");
        mainSource.Should().Contain("ToEntity");
        mainSource.Should().Contain("Start = this.Start");
        mainSource.Should().Contain("End = this.End");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Nested Struct Property
    // =========================================================================

    [Fact]
    public async Task NestedStruct_InClass_MapsWithNestedFromEntity()
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
                         public class Shape
                         {
                             public int Id { get; set; }
                             public Point Center { get; set; }
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

                         [MapFrom<Test.Entities.Shape>]
                         public partial record ShapeDto
                         {
                             public int Id { get; init; }
                             public PointDto Center { get; init; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "ShapeDto.Mapping");
        mainSource.Should().Contain("FromEntity");
        // Nested struct DTO should use FromEntity
        mainSource.Should().Contain("PointDto.FromEntity");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct with Type Conversion
    // =========================================================================

    [Fact]
    public async Task Struct_WithTypeConversion_GeneratesConversion()
    {
        var source = """
                     using System;
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class TimeEntry
                         {
                             public DateTime Start { get; set; }
                             public DateTime End { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.TimeEntry>]
                         public partial struct TimeEntryDto
                         {
                             public DateOnly Start { get; set; }
                             public DateOnly End { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "TimeEntryDto.Mapping");
        mainSource.Should().Contain("public partial struct TimeEntryDto");
        mainSource.Should().Contain("DateOnly.FromDateTime");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct with Nullable Properties
    // =========================================================================

    [Fact]
    public async Task Struct_WithNullableToNonNullable_UsesAutoDefault()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Statistics
                         {
                             public int? Count { get; set; }
                             public decimal? Average { get; set; }
                             public string? Label { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Statistics>]
                         public partial struct StatisticsDto
                         {
                             public int Count { get; set; }
                             public decimal Average { get; set; }
                             public string Label { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "StatisticsDto.Mapping");
        mainSource.Should().Contain("public partial struct StatisticsDto");
        // Should use GetValueOrDefault for nullable value types
        mainSource.Should().Contain("GetValueOrDefault()");
        // Should use ?? "" for nullable string
        mainSource.Should().Contain("?? \"\"");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct Projection with Auto-Default
    // =========================================================================

    [Fact]
    public async Task Struct_Projection_WithNullableToNonNullable_UsesNullCoalescing()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Metric
                         {
                             public int? Value { get; set; }
                             public decimal? Percentage { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Metric>]
                         [GenerateProjection]
                         public partial struct MetricDto
                         {
                             public int Value { get; set; }
                             public decimal Percentage { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var mainSource = GetGeneratedSource(result, "MetricDto.Mapping");
        mainSource.Should().Contain("public partial struct MetricDto");
        mainSource.Should().Contain("Projection { get; }");
        // Projection uses ?? 0 instead of GetValueOrDefault for EF Core
        mainSource.Should().Contain("?? 0");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }

    // =========================================================================
    // Struct with Extensions
    // =========================================================================

    [Fact]
    public async Task Struct_GeneratesExtensionMethods()
    {
        var source = """
                     using Pragmatic.Mapping.Attributes;

                     namespace Test.Entities
                     {
                         public class Size
                         {
                             public int Width { get; set; }
                             public int Height { get; set; }
                         }
                     }
                     namespace Test.Dtos
                     {
                         [MapFrom<Test.Entities.Size>]
                         public partial struct SizeDto
                         {
                             public int Width { get; set; }
                             public int Height { get; set; }
                         }
                     }
                     """;

        var result = RunGenerator(source);
        HasCompilationErrors(result).Should().BeFalse(
            string.Join(", ", GetCompilationErrors(result).Select(d => d.ToString())));

        var extSource = GetGeneratedSource(result, "SizeDto.Extensions");
        extSource.Should().NotBeNull();
        // Extension uses overloads of ToSizeDto for different collection types
        extSource.Should().Contain("ToSizeDto(this global::Test.Entities.Size entity)");
        extSource.Should().Contain("ToSizeDto(this IEnumerable<");
        extSource.Should().Contain("ToSizeDto(this List<");
        extSource.Should().Contain("ToSizeDto(this global::Test.Entities.Size[] entities)");

        await Verify(GetGeneratedSourcesAsDictionary(result));
    }
}
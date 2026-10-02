using Pragmatic.Testing.Assertions;
using System.Text.Json;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Json.Tests;

public class DateRangeConverterJsonTests
{
    private static readonly JsonSerializerOptions Options =
        new JsonSerializerOptions().AddPragmaticTemporal();

    private static readonly DateRange Sample =
        new(new LocalDate(2026, 1, 1), new LocalDate(2026, 3, 31));

    [Fact]
    public void Write_ProducesStartEndObject()
    {
        var json = JsonSerializer.Serialize(Sample, Options);

        json.Should().Contain("\"start\"").And.Contain("\"2026-01-01\"")
            .And.Contain("\"end\"").And.Contain("\"2026-03-31\"");
    }

    [Fact]
    public void Read_ObjectForm_Parses()
    {
        var result = JsonSerializer.Deserialize<DateRange>(
            "{\"start\":\"2026-01-01\",\"end\":\"2026-03-31\"}", Options);

        (result == Sample).Should().BeTrue();
    }

    [Fact]
    public void Read_StringForm_Parses()
    {
        var result = JsonSerializer.Deserialize<DateRange>(
            "\"2026-01-01/2026-03-31\"", Options);

        (result == Sample).Should().BeTrue();
    }

    [Fact]
    public void Read_NullOnNonNullable_ReturnsEmpty()
    {
        var result = JsonSerializer.Deserialize<DateRange>("null", Options);

        result.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Read_NullOnNullable_ReturnsNull()
    {
        var result = JsonSerializer.Deserialize<DateRange?>("null", Options);

        result.Should().BeNull();
    }

    [Fact]
    public void Read_ObjectMissingEnd_Throws()
    {
        var act = () => JsonSerializer.Deserialize<DateRange>(
            "{\"start\":\"2026-01-01\"}", Options);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Read_PropertyNames_AreCaseInsensitive()
    {
        var result = JsonSerializer.Deserialize<DateRange>(
            "{\"Start\":\"2026-01-01\",\"END\":\"2026-03-31\"}", Options);

        (result == Sample).Should().BeTrue();
    }
}

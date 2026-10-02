using System.Globalization;
using System.Text;
using Pragmatic.Testing.Assertions;
using Pragmatic.Persistence.Query.Converters;

namespace Pragmatic.Persistence.Tests.Query.Converters;

/// <summary>
///     Tests for <see cref="JsonQueryConverter{T}" />, the TypeConverter that deserializes a JSON
///     query-string fragment into a FilterDto. Input is untrusted (URL query string), so the
///     converter must guard against oversized / deeply-nested payloads (DoS / stack overflow).
/// </summary>
public class JsonQueryConverterTests
{
    private sealed class LocationFilter
    {
        public string? City { get; set; }
        public NestedFilter? Nested { get; set; }
    }

    private sealed class NestedFilter
    {
        public NestedFilter? Child { get; set; }
        public string? Value { get; set; }
    }

    private static JsonQueryConverter<LocationFilter> Converter() => new();

    [Fact]
    public void CanConvertFrom_StringSourceType_ReturnsTrue()
    {
        Converter().CanConvertFrom(null, typeof(string)).Should().BeTrue();
    }

    [Fact]
    public void CanConvertFrom_NonStringSourceType_ReturnsFalse()
    {
        Converter().CanConvertFrom(null, typeof(int)).Should().BeFalse();
    }

    [Fact]
    public void ConvertFrom_ValidJson_DeserializesUsingWebDefaults()
    {
        // Web defaults => camelCase, case-insensitive matching.
        var result = Converter().ConvertFrom(null, CultureInfo.InvariantCulture, """{"city":"Rome"}""");

        result.Should().BeOfType<LocationFilter>();
        ((LocationFilter)result!).City.Should().Be("Rome");
    }

    [Fact]
    public void ConvertFrom_MixedCaseJson_MatchesCaseInsensitively()
    {
        var result = Converter().ConvertFrom(null, CultureInfo.InvariantCulture, """{"CITY":"Milan"}""");

        ((LocationFilter)result!).City.Should().Be("Milan");
    }

    [Fact]
    public void ConvertFrom_EmptyString_DefersToBaseAndDoesNotDeserialize()
    {
        // Base TypeConverter throws NotSupportedException for an unhandled string conversion.
        var act = () => Converter().ConvertFrom(null, CultureInfo.InvariantCulture, "");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConvertFrom_WhitespaceString_DefersToBaseAndDoesNotDeserialize()
    {
        var act = () => Converter().ConvertFrom(null, CultureInfo.InvariantCulture, "   ");

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConvertFrom_InputAtMaxLength_DeserializesSuccessfully()
    {
        // Build a valid JSON payload padded to exactly the 16 KB boundary so the length guard
        // does not trip. The cap is inclusive of the boundary (only > MaxInputLength is rejected).
        const int maxInputLength = 16 * 1024;
        var prefix = """{"city":""";
        const string suffix = "\"}";
        var padLength = maxInputLength - prefix.Length - suffix.Length;
        var json = prefix + "\"" + new string('a', padLength - 1) + suffix;
        json.Length.Should().Be(maxInputLength);

        var result = Converter().ConvertFrom(null, CultureInfo.InvariantCulture, json);

        ((LocationFilter)result!).City.Should().HaveLength(padLength - 1);
    }

    [Fact]
    public void ConvertFrom_InputExceedingMaxLength_ThrowsArgumentException()
    {
        const int maxInputLength = 16 * 1024;
        var oversized = "\"" + new string('a', maxInputLength) + "\""; // length = maxInputLength + 2

        var act = () => Converter().ConvertFrom(null, CultureInfo.InvariantCulture, oversized);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*maximum allowed length*");
    }

    [Fact]
    public void ConvertFrom_DeeplyNestedJsonBeyondMaxDepth_ThrowsJsonException()
    {
        // MaxDepth is 32. Build nesting well beyond that but under the byte cap so the
        // depth guard (not the length guard) is what rejects it. Each level is "{\"nested\":".
        var builder = new StringBuilder();
        const int depth = 100;
        for (var i = 0; i < depth; i++)
            builder.Append("{\"nested\":");
        builder.Append("null");
        for (var i = 0; i < depth; i++)
            builder.Append('}');

        var json = builder.ToString();
        json.Length.Should().BeLessThan(16 * 1024);

        var act = () => Converter().ConvertFrom(null, CultureInfo.InvariantCulture, json);

        act.Should().Throw<System.Text.Json.JsonException>();
    }

    [Fact]
    public void ConvertFrom_NestingWithinMaxDepth_DeserializesSuccessfully()
    {
        // A handful of nesting levels is well within MaxDepth=32.
        var json = """{"nested":{"child":{"value":"x"}}}""";

        var result = (LocationFilter)Converter().ConvertFrom(null, CultureInfo.InvariantCulture, json)!;

        result.Nested!.Child!.Value.Should().Be("x");
    }

    [Fact]
    public void ConvertFrom_MalformedJson_ThrowsJsonException()
    {
        var act = () => Converter().ConvertFrom(null, CultureInfo.InvariantCulture, "{not valid json");

        act.Should().Throw<System.Text.Json.JsonException>();
    }
}

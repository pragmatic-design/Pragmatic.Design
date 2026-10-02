// =============================================================================
// Pragmatic.Patch - OptionalConverterTests
// Unit tests for OptionalConverter<T>
// =============================================================================

using System.Text.Json;
using Pragmatic.Testing.Assertions;
using Pragmatic.Patch.Serialization;
using Xunit;

namespace Pragmatic.Patch.Tests.Unit;

/// <summary>
///     Tests for <see cref="OptionalConverter{T}"/>.
/// </summary>
public class OptionalConverterTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // OptionalConverter<T> — reference type (string)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionalConverter_Deserialize_PresentString_ReturnsOf()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var result = JsonSerializer.Deserialize<Optional<string>>("\"hello\"", options);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void OptionalConverter_Deserialize_NullToken_ReturnsNull()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var result = JsonSerializer.Deserialize<Optional<string>>("null", options);

        result.HasValue.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void OptionalConverter_Serialize_Null_WritesJsonNull()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var json = JsonSerializer.Serialize(Optional<string>.Null, options);

        json.Should().Be("null");
    }

    [Fact]
    public void OptionalConverter_Serialize_Value_WritesValue()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var json = JsonSerializer.Serialize(Optional<string>.Of("world"), options);

        json.Should().Be("\"world\"");
    }

    [Fact]
    public void OptionalConverter_Serialize_Undefined_WritesNull()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        // Undefined should fall back to null when Write is called directly (the SG converter skips it).
        var json = JsonSerializer.Serialize(Optional<string>.Undefined, options);

        json.Should().Be("null");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // OptionalConverter<T> — value type (int)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void OptionalConverter_ValueType_Deserialize_PresentInt_ReturnsOf()
    {
        var options = BuildOptionsWithTypedConverter<int>();

        var result = JsonSerializer.Deserialize<Optional<int>>("42", options);

        result.HasValue.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void OptionalConverter_ValueType_Deserialize_NullToken_ReturnsNull()
    {
        var options = BuildOptionsWithTypedConverter<int>();

        // JSON null on a value-type Optional<int> → Optional.Null (HasValue=true, Value=default(int)=0)
        var result = JsonSerializer.Deserialize<Optional<int>>("null", options);

        result.HasValue.Should().BeTrue();
    }

    [Fact]
    public void OptionalConverter_ValueType_Serialize_Value_WritesValue()
    {
        var options = BuildOptionsWithTypedConverter<int>();

        var json = JsonSerializer.Serialize(Optional<int>.Of(7), options);

        json.Should().Be("7");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Round-trips through the typed converter
    // ═══════════════════════════════════════════════════════════════════════
    //
    // There is no converter factory: one that built OptionalConverter<T> with MakeGenericType +
    // Activator.CreateInstance could never run under Native AOT, and the generator emits a converter
    // per patch DTO anyway. These assert the typed converter's behaviour.

    [Fact]
    public void OptionalConverter_Roundtrip_String_Of()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var json = JsonSerializer.Serialize(Optional<string>.Of("patch"), options);
        var back = JsonSerializer.Deserialize<Optional<string>>(json, options);

        back.HasValue.Should().BeTrue();
        back.Value.Should().Be("patch");
    }

    [Fact]
    public void OptionalConverter_Roundtrip_String_Null()
    {
        var options = BuildOptionsWithTypedConverter<string>();

        var json = JsonSerializer.Serialize(Optional<string>.Null, options);
        var back = JsonSerializer.Deserialize<Optional<string>>(json, options);

        back.HasValue.Should().BeTrue();
        back.Value.Should().BeNull();
    }

    [Fact]
    public void OptionalConverter_Roundtrip_Int_Of()
    {
        var options = BuildOptionsWithTypedConverter<int>();

        var json = JsonSerializer.Serialize(Optional<int>.Of(99), options);
        var back = JsonSerializer.Deserialize<Optional<int>>(json, options);

        back.HasValue.Should().BeTrue();
        back.Value.Should().Be(99);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-nullable value-type null → Undefined behavior
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Optional_ValueType_NullJson_DeserializesAsNullState()
    {
        // For Optional<int>, receiving JSON null means "explicitly null" (HasValue=true).
        // The Undefined state can only be achieved by the property never being set
        // (i.e., it was never present in the JSON — handled by the SG-generated converter).
        var options = BuildOptionsWithTypedConverter<int>();

        var result = JsonSerializer.Deserialize<Optional<int>>("null", options);

        // JSON null → Optional.Null (HasValue=true), NOT Undefined.
        result.HasValue.Should().BeTrue();
        result.IsUndefined.Should().BeFalse();
    }

    [Fact]
    public void Optional_ValueType_DefaultIsUndefined_NotNull()
    {
        // A value-type Optional<int> that was never set (default) is Undefined, not null.
        Optional<int> opt = default;

        opt.IsUndefined.Should().BeTrue();
        opt.HasValue.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static JsonSerializerOptions BuildOptionsWithTypedConverter<T>()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new OptionalConverter<T>());
        return options;
    }
}

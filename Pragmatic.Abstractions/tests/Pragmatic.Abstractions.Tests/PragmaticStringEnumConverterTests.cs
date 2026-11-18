using System.Text.Json;
using System.Text.Json.Serialization;
using Pragmatic.Serialization;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Abstractions.Tests;

/// <summary>
///     The converter the generated host registers in place of <see cref="JsonStringEnumConverter" /> writes and reads what that one
///     does (#857): replacing it to drop its dynamic-code requirement must not change a single enum on the wire.
/// </summary>
/// <remarks>
///     Through the reflection resolver, on the JIT: the path where this converter hands the enum to the framework one. The generated
///     contexts build <c>JsonStringEnumConverter&lt;TEnum&gt;</c> instead, which is the framework's own and proven by the AOT smoke.
/// </remarks>
public sealed class PragmaticStringEnumConverterTests
{
    public enum Shade
    {
        Plain,
        Bold,
    }

    [Flags]
    public enum Marks
    {
        None = 0,
        Read = 1,
        Starred = 2,
    }

    public sealed record Card(Shade Shade, Marks Marks, Shade? Maybe);

    private static JsonSerializerOptions With(JsonConverter converter) => new(JsonSerializerDefaults.Web)
    {
        Converters = { converter },
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    [Theory]
    [InlineData(Shade.Bold, Marks.Read | Marks.Starred, Shade.Plain)]
    [InlineData(Shade.Plain, Marks.None, null)]
    [InlineData((Shade)7, (Marks)8, (Shade)9)]
    public void Writes_WhatJsonStringEnumConverterWrites(Shade shade, Marks marks, Shade? maybe)
    {
        var card = new Card(shade, marks, maybe);

        JsonSerializer.Serialize(card, With(new PragmaticStringEnumConverter()))
            .Should().Be(JsonSerializer.Serialize(card, With(new JsonStringEnumConverter())));
    }

    [Theory]
    [InlineData("""{"shade":"Bold","marks":"Read, Starred","maybe":"plain"}""")]
    [InlineData("""{"shade":1,"marks":3,"maybe":null}""")]
    public void Reads_WhatJsonStringEnumConverterReads(string json)
        => JsonSerializer.Deserialize<Card>(json, With(new PragmaticStringEnumConverter()))
            .Should().Be(JsonSerializer.Deserialize<Card>(json, With(new JsonStringEnumConverter())));

    [Fact]
    public void AnUnknownName_IsRefused_AsJsonStringEnumConverterRefusesIt()
    {
        var act = () => JsonSerializer.Deserialize<Card>("""{"shade":"Italic","marks":0,"maybe":null}""", With(new PragmaticStringEnumConverter()));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ItClaimsEnums_AndNothingElse()
    {
        var converter = new PragmaticStringEnumConverter();

        converter.CanConvert(typeof(Shade)).Should().BeTrue();
        converter.CanConvert(typeof(int)).Should().BeFalse();
        converter.CanConvert(typeof(string)).Should().BeFalse();
    }
}

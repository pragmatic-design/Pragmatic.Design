using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <c>RawValue</c> embeds a fragment verbatim, so an unchecked caller can silently produce a
///     document that only fails when something downstream parses it. These tests pin the contract:
///     valid JSON goes through untouched, anything else fails at the point of the mistake.
/// </summary>
public sealed class MetadataJsonBuilderTests
{
    [Fact]
    public void RawValue_ValidObject_IsEmbeddedVerbatim()
    {
        var b = new MetadataJsonBuilder();
        b.StartObject().Property("modules").StartArray();
        b.RawValue("""{"name":"Billing","actions":2}""");
        b.EndArray().EndObject();

        var json = b.ToString();

        json.Should().Be("""{"modules":[{"name":"Billing","actions":2}]}""");
        JsonValidator.IsValid(json).Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"name\":}")] // missing value
    [InlineData("{\"name\":\"Billing\"")] // unbalanced
    [InlineData("not json at all")]
    [InlineData("1.2.3")]
    [InlineData("1e+e5")]
    [InlineData("1,234")] // parseable as a decimal, not a JSON number
    [InlineData("\"a\\qb\"")] // illegal escape
    [InlineData("")]
    public void RawValue_InvalidJson_Throws(string rawJson)
    {
        var b = new MetadataJsonBuilder();

        var act = () => b.RawValue(rawJson);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("rawJson")
            .WithMessage("*structurally valid JSON*");
    }

    [Fact]
    public void RawValue_InvalidJson_DoesNotCorruptTheBuilder()
    {
        // The document built so far must stay usable: the throw is reported as PRAG9000 for the
        // offending output, it must not leave half a fragment behind for anything that recovers.
        var b = new MetadataJsonBuilder();
        b.StartObject().Property("ok", true);

        var act = () => b.RawValue("{oops");
        act.Should().Throw<ArgumentException>();

        b.EndObject();
        b.ToString().Should().Be("""{"ok":true}""");
    }

    [Fact]
    public void RawValue_LongInvalidFragment_MessageIsTruncated()
    {
        var b = new MetadataJsonBuilder();
        var huge = new string('x', 5_000);

        var act = () => b.RawValue(huge);

        act.Should().Throw<ArgumentException>()
            .Which.Message.Length.Should().BeLessThan(300);
    }
}

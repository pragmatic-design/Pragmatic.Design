using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Core;

/// <summary>
///     <see cref="JsonValidator" /> gates <c>MetadataJsonBuilder.RawValue</c>, so a false positive here
///     means a corrupt generated manifest/OpenAPI document. These tests pin the string-escape and
///     number grammars, so that <c>"\q"</c>, <c>"\u12"</c>, <c>1.2.3</c> and <c>1e+e5</c> are rejected.
/// </summary>
public sealed class JsonValidatorTests
{
    [Theory]
    // scalars
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("-0")]
    [InlineData("42")]
    [InlineData("-42")]
    [InlineData("1.5")]
    [InlineData("-1.5")]
    [InlineData("1e5")]
    [InlineData("1E5")]
    [InlineData("1e+5")]
    [InlineData("1e-5")]
    [InlineData("1.5e-10")]
    [InlineData("0.0")]
    [InlineData("\"\"")]
    [InlineData("\"plain\"")]
    // every legal escape
    [InlineData("\"a\\\"b\"")]
    [InlineData("\"a\\\\b\"")]
    [InlineData("\"a\\/b\"")]
    [InlineData("\"a\\bb\"")]
    [InlineData("\"a\\fb\"")]
    [InlineData("\"a\\nb\"")]
    [InlineData("\"a\\rb\"")]
    [InlineData("\"a\\tb\"")]
    [InlineData("\"a\\u00E9b\"")]
    [InlineData("\"\\uABCD\\uabcd\\u0123\"")]
    // structures
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("[1,2,3]")]
    [InlineData("[ 1 , 2 ]")]
    [InlineData("{\"a\":1}")]
    [InlineData("{ \"a\" : [ { \"b\" : null } ] }")]
    [InlineData("  {\"a\":\"b\"}  ")]
    public void IsValid_ValidJson_ReturnsTrue(string json)
        => JsonValidator.IsValid(json).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{")]
    [InlineData("[1,")]
    [InlineData("{\"a\"}")]
    [InlineData("{\"a\":}")]
    [InlineData("tru")]
    [InlineData("\"unterminated")]
    [InlineData("{} {}")]
    public void IsValid_StructurallyBroken_ReturnsFalse(string? json)
        => JsonValidator.IsValid(json).Should().BeFalse();

    [Theory]
    [InlineData("\"a\\qb\"")] // \q is not one of the eight legal escapes
    [InlineData("\"\\x\"")]
    [InlineData("\"\\'\"")]
    [InlineData("\"\\\"")] // trailing backslash escapes the closing quote -> unterminated
    [InlineData("\"\\u12\"")] // \u with fewer than four hex digits
    [InlineData("\"\\u123\"")]
    [InlineData("\"\\uZZZZ\"")]
    [InlineData("\"\\u 123\"")]
    public void IsValid_IllegalStringEscape_ReturnsFalse(string json)
        => JsonValidator.IsValid(json).Should().BeFalse();

    [Fact]
    public void IsValid_UnescapedControlCharacterInString_ReturnsFalse()
    {
        // RFC 8259 §7: U+0000..U+001F must be escaped inside a string.
        JsonValidator.IsValid("\"a\nb\"").Should().BeFalse();
        JsonValidator.IsValid("\"a\tb\"").Should().BeFalse();
        JsonValidator.IsValid("\"a\\nb\"").Should().BeTrue();
    }

    [Theory]
    [InlineData("1.2.3")] // two decimal points
    [InlineData("1e+e5")] // exponent marker twice
    [InlineData("1e")] // exponent with no digits
    [InlineData("1e+")]
    [InlineData("1.")] // fraction with no digits
    [InlineData(".5")] // no integer part
    [InlineData("-")]
    [InlineData("--1")]
    [InlineData("+1")] // JSON has no leading plus
    [InlineData("01")] // no leading zeroes
    [InlineData("-01")]
    [InlineData("00")]
    [InlineData("1-2")]
    [InlineData("1,234")] // decimal.TryParse(NumberStyles.Number) accepts this; JSON does not
    public void IsValid_MalformedNumber_ReturnsFalse(string json)
        => JsonValidator.IsValid(json).Should().BeFalse();

    [Theory]
    [InlineData("[1.2.3]")]
    [InlineData("{\"a\":1e+e5}")]
    [InlineData("{\"a\":\"\\q\"}")]
    public void IsValid_MalformedTokenNested_ReturnsFalse(string json)
        => JsonValidator.IsValid(json).Should().BeFalse();
}

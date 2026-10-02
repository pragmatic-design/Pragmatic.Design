using Pragmatic.Testing.Assertions;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Tests.Core;

public sealed class IdentifierHelperTests
{
    [Theory]
    [InlineData("event", "@event")]
    [InlineData("default", "@default")]
    [InlineData("lock", "@lock")]
    [InlineData("class", "@class")]
    [InlineData("string", "@string")]
    public void EscapeIfKeyword_Keyword_IsPrefixed(string input, string expected)
    {
        IdentifierHelper.EscapeIfKeyword(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("orderId")]
    [InlineData("customerName")]
    [InlineData("eventName")] // contains a keyword as a substring but is not one
    public void EscapeIfKeyword_NonKeyword_IsUnchanged(string input)
    {
        IdentifierHelper.EscapeIfKeyword(input).Should().Be(input);
    }
}

using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Documents.Templating.Tests;

/// <summary>
///     An expression is read to its end: text the grammar cannot place is an error, not ignored.
/// </summary>
/// <remarks>
///     The reader stopped wherever the grammar stopped and returned what it had. <c>items[0].name</c>
///     read <c>items</c> and printed the whole list; a pipe argument with a space in it lost everything
///     after the space; a string with no closing quote took the rest of the expression.
/// </remarks>
public class AnExpressionIsReadToItsEndTests
{
    [Fact]
    public void Indexing_IsRefused()
    {
        var failed = Assert.Throws<TemplateParseException>(() => ExpressionParser.ParseExpression("items[0].name"));

        failed.Message.Should().Contain("[0].name");
    }

    [Fact]
    public void APipeArgumentCutAtASpace_IsRefused()
    {
        var failed = Assert.Throws<TemplateParseException>(
            () => ExpressionParser.ParseExpression("issuedOn | date:dd MMM yyyy"));

        failed.Message.Should().Contain("MMM yyyy");
    }

    [Fact]
    public void AStringWithNoClosingQuote_IsRefused()
    {
        Assert.Throws<TemplateParseException>(() => ExpressionParser.ParseExpression("name ?? \"unknown"));
    }

    [Fact]
    public void InATemplate_TheSameIsRefused()
    {
        Assert.Throws<TemplateParseException>(() => ExpressionParser.ParseTemplate("Hi {{ items[0].name }}!"));
    }

    /// <summary>The control: what the grammar reads, with the whitespace around it, still parses.</summary>
    [Theory]
    [InlineData("  customer.name  ")]
    [InlineData("total | currency:\"EUR\"")]
    [InlineData("issuedOn | date:\"dd MMM yyyy\"")]
    [InlineData("paid ? \"Paid\" : \"Due\"")]
    [InlineData("lines.sum(amount) > 100 && !void")]
    [InlineData("name ?? \"unknown\"")]
    public void WhatTheGrammarReads_Parses(string expression)
    {
        ExpressionParser.ParseExpression(expression).Should().NotBeNull();
    }
}

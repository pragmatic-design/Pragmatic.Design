using Pragmatic.Documents.Markup;
using Pragmatic.Email.Templates.Nodes;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
///     A partial is written the same way in both markups, and neither spelling is thrown
///     away in silence.
/// </summary>
/// <remarks>
///     <para>
///         In a <c>.pdxdoc</c> a partial is <c>&lt;import src="header.pdxdoc" /&gt;</c> <b>and</b>
///         <c>&lt;partial name="…" /&gt;</c> — <c>PdxDocParser</c> maps both to the same
///         <c>PartialTemplate</c>. A <c>.pdxemail</c> must do the same: a parser that names
///         <c>&lt;import&gt;</c> and produces nothing drops it with no diagnostic.
///     </para>
///     <para>
///         A mail written beside a letter uses the document's spelling, because that is what the letter
///         uses. Dropped, it renders <b>without its letterhead</b> — no error, no warning, just a body
///         with no heading.
///     </para>
///     <para>
///         ⚠️ <b>Two drop sites, not one</b>: at section level (a direct child of <c>&lt;email&gt;</c>)
///         as well as inside a column, and for both spellings. A partial is resolved as a node inside a
///         column, so one that never becomes a node is never resolved — "handled at resolve time" is not
///         true of a partial the parser skipped.
///     </para>
/// </remarks>
public class TheTwoMarkupsAgreeOnAPartialTests
{
    /// <summary>The setpoint: the document's spelling works in an email too.</summary>
    [Fact]
    public void AnImportInsideASection_IsThePartialItNames()
    {
        var template = PdxEmailParser.Parse("""
            <email subject="Decision">
                <article>
                    <import src="mail-header.pdxemail" />
                    <text>Body</text>
                </article>
            </email>
            """);

        template.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailPartialTemplate>()
            .Which.Name.Should().Be("mail-header.pdxemail",
                "the src as written, exactly as PdxDocParser passes it on — what to make of the name is "
                + "the partial provider's business, and the document's already strips the extension");
    }

    /// <summary>
    ///     And at the top level, for both spellings.
    /// </summary>
    [Theory]
    [InlineData("<import src=\"mail-header\" />")]
    [InlineData("<partial name=\"mail-header\" />")]
    public void APartialAtTheTopLevel_IsASectionOfItsOwn(string element)
    {
        var template = PdxEmailParser.Parse($"""
            <email subject="Decision">
                {element}
                <article>
                    <text>Body</text>
                </article>
            </email>
            """);

        template.Sections.Should().HaveCount(2,
            "the partial is a section of its own, like any other single element at this level");
        template.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailPartialTemplate>()
            .Which.Name.Should().Be("mail-header");
    }

    /// <summary>
    ///     The two spellings are one thing: whatever the name is read from, what comes out is the same
    ///     node.
    /// </summary>
    [Fact]
    public void TheTwoSpellings_ProduceTheSamePartial()
    {
        var fromImport = PartialIn("""<import src="mail-header" />""");
        var fromPartial = PartialIn("""<partial name="mail-header" />""");

        fromImport.Name.Should().Be(fromPartial.Name);
    }

    /// <summary>
    ///     The control: a partial with nothing to name is refused, not dropped. <c>&lt;partial&gt;</c>
    ///     has always thrown without its <c>name</c>; <c>&lt;import&gt;</c> now does the same without
    ///     its <c>src</c>.
    /// </summary>
    /// <remarks>
    ///     Without this, "import is honoured" would be satisfied by a parser that reads a missing
    ///     attribute as an empty name and asks the provider for "" — a lookup that fails much later,
    ///     naming nothing.
    /// </remarks>
    [Fact]
    public void AnImportWithNoSource_IsRefused()
    {
        var act = () => PdxEmailParser.Parse("""
            <email subject="Decision">
                <article>
                    <import />
                </article>
            </email>
            """);

        var failed = Assert.Throws<MarkupParseException>(act);
        failed.Message.Should().Contain("src");
    }

    private static EmailPartialTemplate PartialIn(string element)
        => (EmailPartialTemplate)PdxEmailParser.Parse($"""
            <email subject="Decision">
                <article>
                    {element}
                </article>
            </email>
            """).Sections[0].Columns[0].Content[0];
}

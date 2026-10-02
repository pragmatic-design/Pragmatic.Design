using Pragmatic.Documents.Markup;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Documents.Markup.Tests;

/// <summary>
///     An element or attribute the markup does not know is refused with the name of what was written,
///     in both markups.
/// </summary>
/// <remarks>
///     A parser that drops what it does not recognise turns an unknown element into nothing and never
///     reads an unknown attribute. A template written from an old page of the docs — <c>&lt;section&gt;</c>,
///     <c>data-sorce</c> — would render empty or without its binding, and say nothing.
/// </remarks>
public class WhatTheMarkupDoesNotKnowIsRefusedTests
{
    [Fact]
    public void AnUnknownElement_InADocument_IsRefused()
    {
        var failed = Assert.Throws<MarkupParseException>(
            () => PdxDocParser.Parse("<document><page><section>Hello</section></page></document>"));

        failed.Message.Should().Contain("<section>");
    }

    [Fact]
    public void AnUnknownChild_OfATable_IsRefused()
    {
        var failed = Assert.Throws<MarkupParseException>(() => PdxDocParser.Parse("""
            <document><page>
              <table data-source="lines"><column>Item</column><rows /></table>
            </page></document>
            """));

        failed.Message.Should().Contain("<rows>");
    }

    [Fact]
    public void AnUnknownAttribute_InADocument_IsRefused()
    {
        var failed = Assert.Throws<MarkupParseException>(() => PdxDocParser.Parse("""
            <document><page>
              <table data-sorce="lines"><column>Item</column></table>
            </page></document>
            """));

        failed.Message.Should().Contain("data-sorce").And.Contain("<table>");
    }

    [Fact]
    public void AnUnknownElement_InAnEmail_IsRefused()
    {
        var failed = Assert.Throws<MarkupParseException>(
            () => PdxEmailParser.Parse("<email><row><social-bar /></row></email>"));

        failed.Message.Should().Contain("<social-bar>");
    }

    [Fact]
    public void AnUnknownAttribute_InAnEmail_IsRefused()
    {
        var failed = Assert.Throws<MarkupParseException>(
            () => PdxEmailParser.Parse("""<email><row><button url="https://example.com">Go</button></row></email>"""));

        failed.Message.Should().Contain("url").And.Contain("<button>");
    }

    /// <summary>The control: the vocabulary the docs teach parses, directives included.</summary>
    [Fact]
    public void TheKnownVocabulary_Parses()
    {
        var document = PdxDocParser.Parse("""
            <document title="T" author="A" lang="it" page-size="A4" orientation="portrait" margin="20">
              <page>
                <header><text>Head</text></header>
                <heading level="2" if="show">Title</heading>
                <paragraph><text for="x in xs">{{ x }}</text></paragraph>
                <table data-source="lines" repeat-header="true">
                  <column width="50" align="right">Item</column>
                  <header><cell>Item</cell></header>
                  <row-template><cell colspan="1" rowspan="1">{{ item.name }}</cell></row-template>
                  <row><cell>Total</cell></row>
                </table>
                <list ordered="true" data-source="xs"><list-item>One</list-item><item-template>{{ item }}</item-template></list>
                <image src="logo.png" alt="Logo" width="10" height="10" />
                <hr thickness="1" /><spacer height="5" /><pagebreak />
                <container><barcode value="1" type="code128" width="10" height="5" /></container>
                <for-each source="xs" item="x"><text>{{ x }}</text></for-each>
                <partial name="p" /><import src="header.pdxdoc" />
                <footer><text>Foot</text></footer>
              </page>
            </document>
            """);
        var email = PdxEmailParser.Parse("""
            <email subject="S" preheader="P" lang="it" width="600" background="#fff" wrapper-background="#eee"
                   font-family="Arial" font-size="16" text-color="#333">
              <hero background="#000" padding="30"><heading level="1" align="center" color="#fff">Hi</heading></hero>
              <row background="#fff" padding="10" if="show">
                <col width="6" valign="middle" padding="5">
                  <text align="left" color="#000" size="14">Text</text>
                  <image src="x.png" alt="X" width="10" height="10" link="https://example.com" align="center" />
                  <button href="https://example.com" background="#00f" color="#fff" radius="4" font-size="16" align="center">Go</button>
                </col>
              </row>
              <article background="#fff" padding="10"><spacer height="10" /><divider color="#ccc" thickness="1" /></article>
              <row>
                <table data-source="lines" border="#ccc" padding="8">
                  <column width="50" align="right">Item</column>
                  <row-template><cell bold="true" color="#000" align="right" colspan="1">{{ item.name }}</cell></row-template>
                  <row background="#eee"><cell>Total</cell></row>
                </table>
                <partial name="p" /><import src="mail-header.pdxemail" />
              </row>
              <footer background="#eee" padding="10"><text>Foot</text></footer>
            </email>
            """);

        document.Pages.Should().ContainSingle();
        email.Sections.Should().HaveCount(5);
    }
}

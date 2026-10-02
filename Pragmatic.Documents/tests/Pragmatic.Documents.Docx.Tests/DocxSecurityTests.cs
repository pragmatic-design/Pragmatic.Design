using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>Security and TOC-integrity fixes for the DOCX renderer (Cluster B, wave B4).</summary>
public class DocxSecurityTests
{
    [Fact]
    public void Hyperlink_WithJavascriptScheme_CreatesNoExternalRelationship()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new HyperlinkNode { Href = "javascript:alert(1)", Children = [new TextNode { Content = "click" }] }
            ] }],
        };

        var docx = DocxRenderer.Render(model);
        var rels = DocxTestHelper.GetPart(docx, "word/_rels/document.xml.rels").ToString();
        var body = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        rels.Should().NotContain("javascript:alert(1)");
        // The link text is still rendered — only the dangerous target is dropped.
        body.Should().Contain("click");
    }

    [Fact]
    public void Hyperlink_WithHttpsScheme_CreatesExternalRelationship()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new HyperlinkNode { Href = "https://example.com", Children = [new TextNode { Content = "ok" }] }
            ] }],
        };

        var rels = DocxTestHelper.GetPart(DocxRenderer.Render(model), "word/_rels/document.xml.rels").ToString();

        rels.Should().Contain("https://example.com");
    }

    [Fact]
    public void FieldNode_DateFormatWithQuoteInjection_IsStripped()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new FieldNode { FieldType = FieldType.Date, Format = "yyyy\" FILENAME \"" }
            ] }],
        };

        var body = DocxTestHelper.GetPart(DocxRenderer.Render(model), "word/document.xml").ToString();

        // The embedded quotes are stripped, so the injected FILENAME cannot become its own switch:
        // the field stays a single well-formed DATE instruction.
        body.Should().Contain("DATE \\@ \"yyyy FILENAME \"");
    }

    [Fact]
    public void Heading_InsideTableCell_IsDiscoveredByToc()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new TocNode { MaxLevel = 3 },
                new TableNode { Rows = [new TableRow { Cells = [new TableCell { Content = [
                    new HeadingNode { Content = "HeadingInCell", Level = 1 }
                ] }] }] },
            ] }],
        };

        var body = DocxTestHelper.GetPart(
            DocxRenderer.Render(model, options: new DocxRenderOptions { UpdateFieldsOnOpen = false }),
            "word/document.xml").ToString();

        // The heading text appears twice: once in the table cell, once as a static TOC entry.
        // A pre-scan that skipped table cells would leave the TOC entry out.
        var occurrences = body.Split("HeadingInCell").Length - 1;
        occurrences.Should().BeGreaterThanOrEqualTo(2);
    }
}

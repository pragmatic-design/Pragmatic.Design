using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Tests;

public class PdfNewNodesTests
{
    [NativeRequiredFact]
    public void Render_WithToc_ProducesPdf()
    {
        var model = new DocumentBuilder()
            .Title("Document with TOC")
            .Page(p => p
                .Toc(maxLevel: 3, title: "Table of Contents")
                .Heading("Chapter 1", level: 1)
                .Text("Content of chapter 1.")
                .Heading("Section 1.1", level: 2)
                .Text("Content of section 1.1.")
                .Heading("Chapter 2", level: 1)
                .Text("Content of chapter 2.")
            )
            .Build();

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_WithFootnote_ProducesPdf()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [
                new ParagraphNode
                {
                    Children = [
                        new TextNode { Content = "This statement needs a reference" },
                        new FootnoteNode { Content = "See Smith et al., 2025." }
                    ]
                }
            ] }]
        };

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_WithHyperlink_ProducesPdf()
    {
        var model = new DocumentBuilder()
            .Title("Links")
            .Page(p => p
                .Hyperlink("https://pragmatic.design", "Visit Pragmatic Design")
            )
            .Build();

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_WithPageNumberField_ProducesPdf()
    {
        var model = new DocumentBuilder()
            .Title("Page Numbers")
            .Page(p => p
                .Heading("Page 1")
                .Text("Some content")
            )
            .Build();

        // Add footer with page number field
        var page = new DocumentPage
        {
            Footer = [
                new ParagraphNode
                {
                    Children = [
                        new TextNode { Content = "Page " },
                        new FieldNode { FieldType = FieldType.Page },
                        new TextNode { Content = " of " },
                        new FieldNode { FieldType = FieldType.NumPages }
                    ],
                    Style = new NodeStyle { TextAlign = TextAlign.Center }
                }
            ],
            Content = [
                new HeadingNode { Content = "Document with Page Numbers" },
                new TextNode { Content = "This document has page numbers in the footer." }
            ]
        };

        var doc = new DocumentModel { Pages = [page] };

        var pdf = PdfRenderer.Render(doc);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_ComplexDocument_WithAllNewNodes()
    {
        var model = new DocumentBuilder()
            .Title("Complete Document")
            .Author("Pragmatic")
            .Subject("Test document with all new node types")
            .Page(p => p
                .Toc(maxLevel: 2, title: "Contents")
                .Heading("Introduction", level: 1)
                .Text("Welcome to this document.")
                .Hyperlink("https://example.com", "External link")
                .Heading("Details", level: 2)
                .Text("Some detailed text with a footnote.")
                .Footnote("This is a footnote explaining the detail.")
                .Heading("Conclusion", level: 1)
                .Text("End of document.")
            )
            .Build();

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        pdf.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }
}

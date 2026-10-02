using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Tests;

public class DocxRendererTests
{

    [Fact]
    public void Render_EmptyDocument_ProducesValidZip()
    {
        var model = new DocumentModel { Title = "Empty" };

        var docx = DocxRenderer.Render(model);

        // ZIP magic bytes: PK (50 4B)
        docx.Should().NotBeEmpty();
        docx[0].Should().Be(0x50);
        docx[1].Should().Be(0x4B);
    }

    [Fact]
    public void Render_EmptyDocument_ContainsRequiredParts()
    {
        var model = new DocumentModel { Title = "Parts Test" };

        var docx = DocxRenderer.Render(model);
        var entries = DocxTestHelper.GetEntryNames(docx);

        entries.Should().Contain("[Content_Types].xml");
        entries.Should().Contain("_rels/.rels");
        entries.Should().Contain("word/document.xml");
        entries.Should().Contain("word/styles.xml");
        entries.Should().Contain("word/settings.xml");
        entries.Should().Contain("word/fontTable.xml");
        entries.Should().Contain("word/_rels/document.xml.rels");
        entries.Should().Contain("docProps/core.xml");
        entries.Should().Contain("docProps/app.xml");
    }

    [Fact]
    public void Render_WithTitle_SetsDocProperties()
    {
        var model = new DocumentModel
        {
            Title = "Test Invoice",
            Author = "Pragmatic",
            Subject = "Testing",
            Language = "it-IT"
        };

        var docx = DocxRenderer.Render(model);
        var coreXml = DocxTestHelper.GetPart(docx, "docProps/core.xml");
        var xml = coreXml.ToString();

        xml.Should().Contain("Test Invoice");
        xml.Should().Contain("Pragmatic");
        xml.Should().Contain("Testing");
        xml.Should().Contain("it-IT");
    }

    [Fact]
    public void Render_SimpleText_ProducesDocumentXml()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content = [new TextNode { Content = "Hello World" }]
            }]
        };

        var docx = DocxRenderer.Render(model);
        var documentXml = DocxTestHelper.GetPart(docx, "word/document.xml");
        var xml = documentXml.ToString();

        xml.Should().Contain("Hello World");
    }

    [Fact]
    public void Render_Heading_UsesHeadingStyle()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content = [new HeadingNode { Content = "Chapter 1", Level = 1 }]
            }]
        };

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("Heading1");
        xml.Should().Contain("Chapter 1");
    }

    [Fact]
    public void Render_Table_ProducesTableMarkup()
    {
        var model = new DocumentBuilder()
            .Page(p => p.Table(t => t
                .Column(width: 80)
                .Column(width: 40)
                .HeaderRow("Item", "Price")
                .Row("Widget", "€99")))
            .Build();

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("tbl");
        xml.Should().Contain("Widget");
        xml.Should().Contain("€99");
    }

    [Fact]
    public void Render_List_ProducesNumberingRef()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content = [new ListNode
                {
                    Ordered = true,
                    Items = [
                        new ListItem { Content = [new TextNode { Content = "First" }] },
                        new ListItem { Content = [new TextNode { Content = "Second" }] }
                    ]
                }]
            }]
        };

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("numPr");
        xml.Should().Contain("First");
        xml.Should().Contain("Second");

        // Numbering definition should exist
        DocxTestHelper.HasPart(docx, "word/numbering.xml").Should().BeTrue();
    }

    [Fact]
    public void Render_MultiPage_CreatesSections()
    {
        var model = new DocumentBuilder()
            .Page(p => p.Heading("Page 1"))
            .Page(p => p.Heading("Page 2"))
            .Build();

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("Page 1");
        xml.Should().Contain("Page 2");
        xml.Should().Contain("sectPr");
    }

    [Fact]
    public void Render_Hyperlink_ProducesHyperlinkMarkup()
    {
        var model = new DocumentBuilder()
            .Page(p => p.Hyperlink("https://pragmatic.design", "Visit us"))
            .Build();

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("hyperlink");
        xml.Should().Contain("Visit us");
    }

    [Fact]
    public void Render_Toc_ProducesFieldCode()
    {
        var model = new DocumentBuilder()
            .Page(p => p
                .Toc(maxLevel: 3, title: "Contents")
                .Heading("Chapter 1")
                .Text("Text"))
            .Build();

        var docx = DocxRenderer.Render(model);
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        xml.Should().Contain("TOC");
        xml.Should().Contain("fldChar");
    }

    [Fact]
    public void Render_Footnote_CreatesFootnotesXml()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content = [
                    new ParagraphNode
                    {
                        Children = [
                            new TextNode { Content = "Some text" },
                            new FootnoteNode { Content = "A footnote." }
                        ]
                    }
                ]
            }]
        };

        var docx = DocxRenderer.Render(model);

        DocxTestHelper.HasPart(docx, "word/footnotes.xml").Should().BeTrue();
        var xml = DocxTestHelper.GetPart(docx, "word/footnotes.xml").ToString();
        xml.Should().Contain("A footnote.");
    }

    [Fact]
    public void Render_HeaderFooter_CreatesHeaderFooterParts()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Header = [new TextNode { Content = "My Header" }],
                Footer = [new TextNode { Content = "My Footer" }],
                Content = [new TextNode { Content = "Body" }]
            }]
        };

        var docx = DocxRenderer.Render(model);
        var entries = DocxTestHelper.GetEntryNames(docx);

        entries.Should().Contain(e => e.StartsWith("word/header"));
        entries.Should().Contain(e => e.StartsWith("word/footer"));
    }

    [Fact]
    public void Render_FieldNode_ProducesFieldCode()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Footer = [new ParagraphNode
                {
                    Children = [
                        new TextNode { Content = "Page " },
                        new FieldNode { FieldType = FieldType.Page },
                        new TextNode { Content = " of " },
                        new FieldNode { FieldType = FieldType.NumPages }
                    ]
                }],
                Content = [new TextNode { Content = "Body" }]
            }]
        };

        var docx = DocxRenderer.Render(model);
        docx.Should().NotBeEmpty();
        // The footer should contain PAGE and NUMPAGES field codes
        var entries = DocxTestHelper.GetEntryNames(docx);
        entries.Should().Contain(e => e.StartsWith("word/footer"));
    }

    [Fact]
    public async Task RenderAsync_ProducesValidDocx()
    {
        var model = new DocumentBuilder()
            .Title("Async Test")
            .Page(p => p.Text("Async content"))
            .Build();

        var docx = await DocxRenderer.RenderAsync(model);

        docx[0].Should().Be(0x50);
        docx[1].Should().Be(0x4B);
    }

    [Fact]
    public async Task RenderToStreamAsync_WritesToStream()
    {
        var model = new DocumentBuilder()
            .Title("Stream Test")
            .Page(p => p.Text("Stream content"))
            .Build();

        using var ms = new MemoryStream();
        await DocxRenderer.RenderToStreamAsync(ms, model);

        ms.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Render_ComplexInvoice_ProducesValidDocx()
    {
        var model = new DocumentBuilder()
            .Title("Fattura #2026-042")
            .Author("Pragmatic S.r.l.")
            .Subject("Invoice")
            .Keywords("fattura, pragmatic")
            .Language("it-IT")
            .Page(p => p
                .Heading("Fattura #2026-042", level: 1)
                .Text("Cliente: Mario Rossi")
                .Text("Data: 10/04/2026")
                .HorizontalRule()
                .Table(t => t
                    .Column(width: 100)
                    .Column(width: 40)
                    .HeaderRow("Descrizione", "Importo")
                    .Row("Camera Deluxe (3 notti)", "€450,00")
                    .Row("Colazione buffet", "€90,00"))
                .Spacer(5)
                .Text("Totale: €540,00")
                .Footnote("Tutti i prezzi sono IVA inclusa."))
            .Build();

        var docx = DocxRenderer.Render(model);

        docx.Should().NotBeEmpty();
        docx.Length.Should().BeGreaterThan(1000);
        // Valid ZIP
        docx[0].Should().Be(0x50);
        docx[1].Should().Be(0x4B);

        // Contains all expected parts
        var entries = DocxTestHelper.GetEntryNames(docx);
        entries.Should().Contain("word/document.xml");
        entries.Should().Contain("word/footnotes.xml");
    }

    [Fact]
    public void Render_Table_RowSpanPlusColSpan_ProducesValidOoxml()
    {
        // 3×3 table where cell (0,0) has ColSpan=2 + RowSpan=2
        // Expected OOXML: rows 1 has continuation cells with vMerge + gridSpan=2
        var model = new DocumentBuilder()
            .Page(p => p.Table(t =>
            {
                t.Column().Column().Column();
                t.Row(new TableRow
                {
                    Cells =
                    [
                        new TableCell { Content = [new TextNode { Content = "Merged" }], ColSpan = 2, RowSpan = 2 },
                        new TableCell { Content = [new TextNode { Content = "C" }] }
                    ]
                });
                t.Row(new TableRow
                {
                    // Only 1 cell because col 0-1 are covered by the rowspan+colspan above
                    Cells = [new TableCell { Content = [new TextNode { Content = "F" }] }]
                });
                t.Row(new TableRow
                {
                    Cells =
                    [
                        new TableCell { Content = [new TextNode { Content = "G" }] },
                        new TableCell { Content = [new TextNode { Content = "H" }] },
                        new TableCell { Content = [new TextNode { Content = "I" }] }
                    ]
                });
            }))
            .Build();

        var docx = DocxRenderer.Render(model);
        docx.Should().NotBeEmpty();

        // Verify OOXML structure
        var xml = DocxTestHelper.GetPart(docx, "word/document.xml").ToString();

        // Row 0: cell with gridSpan=2 + vMerge restart, plus cell C
        xml.Should().Contain("w:gridSpan");
        xml.Should().Contain("w:vMerge");

        // Row 1: continuation cell with vMerge (no val) + gridSpan=2, plus cell F
        // The vMerge without val="restart" is the continuation marker
        var vMergeCount = System.Text.RegularExpressions.Regex.Matches(xml, "w:vMerge").Count;
        vMergeCount.Should().BeGreaterThanOrEqualTo(2, "need restart + continuation vMerge");
    }
}

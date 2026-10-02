using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Model.Tests;

public class DocumentBuilderTests
{
    [Fact]
    public void Builder_InvoiceDocument_ProducesValidModel()
    {
        var doc = new DocumentBuilder()
            .Title("Invoice #2026-001")
            .Author("Pragmatic S.r.l.")
            .Language("it-IT")
            .WithMargins(new Margins { Top = 20, Right = 15, Bottom = 20, Left = 15 })
            .Page(p => p
                .Heading("FATTURA", level: 1)
                .Spacer(10)
                .Text("Numero: 2026-001")
                .Text("Data: 09/04/2026")
                .HorizontalRule()
                .Table(t => t
                    .Column(width: 80)
                    .Column(width: 30, align: TextAlign.Right)
                    .HeaderRow("Descrizione", "Importo")
                    .Row("Licenza Pragmatic Enterprise", "€ 5.000,00")
                    .Row("Supporto annuale", "€ 1.200,00")
                )
                .Spacer(10)
                .Paragraph(
                    new TextNode { Content = "Totale: " },
                    new TextNode { Content = "€ 6.200,00", Style = new NodeStyle { FontWeight = FontWeight.Bold } }
                )
                .Barcode("INV-2026-001", BarcodeType.Code128)
            )
            .Build();

        doc.Title.Should().Be("Invoice #2026-001");
        doc.Pages.Should().HaveCount(1);

        var content = doc.Pages[0].Content;
        content[0].Should().BeOfType<HeadingNode>();
        content[1].Should().BeOfType<SpacerNode>();
        content[4].Should().BeOfType<HorizontalRuleNode>();
        content[5].Should().BeOfType<TableNode>();
        content[^1].Should().BeOfType<BarcodeNode>();

        // Roundtrip
        var json = DocumentSerializer.Serialize(doc);
        var deserialized = DocumentSerializer.Deserialize(json);
        deserialized!.Title.Should().Be("Invoice #2026-001");
        deserialized.Pages[0].Content.Should().HaveCount(content.Count);
    }

    [Fact]
    public void Builder_HeaderFooter_Works()
    {
        var doc = new DocumentBuilder()
            .Title("Report")
            .Page(p => p
                .Header(new TextNode { Content = "Company Report — Confidential" })
                .Footer(new TextNode { Content = "Page 1 of 1" })
                .Text("Report content goes here.")
            )
            .Build();

        var page = doc.Pages[0];
        page.Header.Should().HaveCount(1);
        page.Footer.Should().HaveCount(1);
        page.Content.Should().HaveCount(1);
    }

    [Fact]
    public void Builder_MultiPage_Works()
    {
        var doc = new DocumentBuilder()
            .Title("Multi-page")
            .Page(p => p.Heading("Page 1"))
            .Page(p => p.Heading("Page 2"))
            .Page(p => p.Heading("Page 3"))
            .Build();

        doc.Pages.Should().HaveCount(3);
    }

    [Fact]
    public void Builder_WithMetadata_Works()
    {
        var doc = new DocumentBuilder()
            .Title("Meta Test")
            .Meta("creator", "Pragmatic.Documents")
            .Meta("version", "1.0")
            .Page(p => p.Text("Content"))
            .Build();

        doc.Metadata.Should().NotBeNull();
        doc.Metadata!["creator"].Should().Be("Pragmatic.Documents");
        doc.Metadata["version"].Should().Be("1.0");
    }

    [Fact]
    public void Builder_Landscape_Works()
    {
        var doc = new DocumentBuilder()
            .Landscape()
            .Size(PageSize.A3)
            .Page(p => p.Text("Wide content"))
            .Build();

        doc.Orientation.Should().Be(PageOrientation.Landscape);
        doc.PageSize.Should().Be(PageSize.A3);
    }

    [Fact]
    public void Builder_CreatedDate_FlowsToModel()
    {
        var created = new DateTimeOffset(2026, 1, 15, 9, 30, 0, TimeSpan.Zero);

        var doc = new DocumentBuilder()
            .Title("Dated")
            .CreatedDate(created)
            .Page(p => p.Text("Content"))
            .Build();

        doc.CreatedDate.Should().Be(created);
    }

    [Fact]
    public void PageBuilder_PerPageOverrides_FlowToPage()
    {
        var margins = new Margins { Top = 5, Right = 5, Bottom = 5, Left = 5 };

        var doc = new DocumentBuilder()
            .Size(PageSize.A4)
            .Page(p => p
                .Size(PageSize.A3)
                .Landscape()
                .WithMargins(margins)
                .Text("Wide page in an A4 document"))
            .Build();

        var page = doc.Pages[0];
        page.PageSize.Should().Be(PageSize.A3);
        page.Orientation.Should().Be(PageOrientation.Landscape);
        page.Margins.Should().Be(margins);
        // Document-level defaults remain untouched.
        doc.PageSize.Should().Be(PageSize.A4);
    }

    [Fact]
    public void PageBuilder_FirstPageHeaderFooter_EnablesDifferentFirstPage()
    {
        var doc = new DocumentBuilder()
            .Page(p => p
                .Header(new TextNode { Content = "Standard header" })
                .Footer(new TextNode { Content = "Standard footer" })
                .FirstPageHeader(new TextNode { Content = "Cover header" })
                .FirstPageFooter(new TextNode { Content = "Cover footer" })
                .Text("Body"))
            .Build();

        var page = doc.Pages[0];
        page.DifferentFirstPage.Should().BeTrue();
        page.FirstPageHeader.Should().ContainSingle();
        page.FirstPageFooter.Should().ContainSingle();
        page.Header.Should().ContainSingle();
    }

    [Fact]
    public void PageBuilder_DifferentFirstPage_WithoutContent_MarksFlagOnly()
    {
        var doc = new DocumentBuilder()
            .Page(p => p
                .Header(new TextNode { Content = "Header from page 2 onward" })
                .DifferentFirstPage()
                .Text("Body"))
            .Build();

        var page = doc.Pages[0];
        page.DifferentFirstPage.Should().BeTrue();
        page.FirstPageHeader.Should().BeNull();
        page.FirstPageFooter.Should().BeNull();
    }

    [Fact]
    public void Build_ThenMutateBuilder_DoesNotAffectBuiltModel()
    {
        var builder = new DocumentBuilder()
            .Meta("k", "v")
            .Page(p => p.Text("one"));

        var doc = builder.Build();

        // Continue using the builder after Build(): the already-built model must not change.
        builder.Page(p => p.Text("two")).Meta("k2", "v2");

        doc.Pages.Should().HaveCount(1);
        doc.Metadata.Should().ContainSingle();
    }
}

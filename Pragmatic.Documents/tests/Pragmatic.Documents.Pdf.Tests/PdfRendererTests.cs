using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>Skip test if the native library is not available.</summary>
public sealed class NativeRequiredFact : FactAttribute
{
    public NativeRequiredFact()
    {
        try
        {
            var model = new DocumentModel
            {
                Title = "Test",
                Pages = [new DocumentPage { Content = [new TextNode { Content = "X" }] }]
            };
            PdfRenderer.Render(model);
        }
        catch (DllNotFoundException)
        {
            Skip = "Native library (pragmatic_native) not available on this platform.";
        }
        catch
        {
            // DLL loaded, other error = native is available
        }
    }
}

public class PdfRendererTests
{
    [NativeRequiredFact]
    public void Render_SimpleDocument_ProducesPdf()
    {
        var model = new DocumentModel
        {
            Title = "Test Document",
            Pages = [new DocumentPage
            {
                Content =
                [
                    new HeadingNode { Content = "Hello World", Level = 1 },
                    new TextNode { Content = "This is a test document." }
                ]
            }]
        };

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        pdf[0].Should().Be((byte)'%');
        pdf[1].Should().Be((byte)'P');
        pdf[2].Should().Be((byte)'D');
        pdf[3].Should().Be((byte)'F');
    }

    [NativeRequiredFact]
    public void Render_InvoiceDocument_ProducesPdf()
    {
        var model = new DocumentBuilder()
            .Title("Fattura #2026-042")
            .Author("Pragmatic S.r.l.")
            .Language("it-IT")
            .Page(p => p
                .Heading("Fattura #2026-042", level: 1)
                .Text("Cliente: Mario Rossi")
                .Text("Data: 09/04/2026")
                .HorizontalRule()
                .Table(t => t
                    .Column(width: 100)
                    .Column(width: 40, align: TextAlign.Right)
                    .HeaderRow("Descrizione", "Importo")
                    .Row("Camera Deluxe (3 notti)", "€450,00")
                    .Row("Colazione buffet", "€90,00")
                )
                .Text("Totale: €540,00")
            )
            .Build();

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        pdf.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_MultiPageDocument()
    {
        var model = new DocumentBuilder()
            .Title("Multi-page")
            .Page(p => p.Heading("Page 1"))
            .Page(p => p.Heading("Page 2"))
            .Page(p => p.Heading("Page 3"))
            .Build();

        var pdf = PdfRenderer.Render(model);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void Render_DocumentWithTable()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content =
                [
                    new TableNode
                    {
                        Columns = [new TableColumn { Width = 80 }, new TableColumn { Width = 40 }],
                        Header = new TableRow
                        {
                            Cells =
                            [
                                new TableCell { Content = [new TextNode { Content = "Item" }] },
                                new TableCell { Content = [new TextNode { Content = "Price" }] }
                            ]
                        },
                        Rows = [new TableRow { Cells = [
                            new TableCell { Content = [new TextNode { Content = "Widget" }] },
                            new TableCell { Content = [new TextNode { Content = "€99" }] }
                        ] }]
                    }
                ]
            }]
        };

        var pdf = PdfRenderer.Render(model);
        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public void RenderTo_Stream()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [new TextNode { Content = "Stream" }] }]
        };

        using var ms = new MemoryStream();
        PdfRenderer.RenderTo(ms, model);
        ms.Length.Should().BeGreaterThan(0);
    }

    [NativeRequiredFact]
    public void Render_WithList()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage
            {
                Content =
                [
                    new ListNode
                    {
                        Ordered = true,
                        Items =
                        [
                            new ListItem { Content = [new TextNode { Content = "First" }] },
                            new ListItem { Content = [new TextNode { Content = "Second" }] }
                        ]
                    }
                ]
            }]
        };

        var pdf = PdfRenderer.Render(model);
        pdf.Should().NotBeEmpty();
    }
}

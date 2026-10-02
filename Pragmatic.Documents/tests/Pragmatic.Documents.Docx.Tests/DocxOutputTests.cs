using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// Generates real DOCX files for manual quality inspection.
/// Output: tests/output/*.docx — open in Word or LibreOffice to verify.
/// </summary>
public class DocxOutputTests
{
    private static readonly string OutputDir = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "output");

    private static TableRow MakeRow(params string[] cells)
        => new() { Cells = cells.Select(c => new TableCell { Content = [new TextNode { Content = c }] }).ToList() };

    private static void SaveDocx(byte[] docx, string fileName)
    {
        Directory.CreateDirectory(OutputDir);
        var path = Path.Combine(OutputDir, fileName);
        File.WriteAllBytes(path, docx);
        Console.WriteLine($"DOCX saved: {Path.GetFullPath(path)} ({docx.Length:N0} bytes)");
    }

    [Fact]
    public void Output_CompleteInvoice()
    {
        var model = new DocumentBuilder()
            .Title("Fattura #2026-042")
            .Author("Pragmatic S.r.l.")
            .Subject("Fattura commerciale")
            .Keywords("fattura, pragmatic, 2026")
            .Language("it-IT")
            .WithMargins(new Margins { Top = 25, Right = 20, Bottom = 25, Left = 20 })
            .Page(p => p
                .Header(new TextNode
                {
                    Content = "PRAGMATIC S.R.L. — Via Roma 42, 20121 Milano",
                    Style = new NodeStyle { FontSize = 9, Color = "#666666" }
                })
                .Heading("FATTURA N. 2026-042", level: 1)
                .Spacer(3)
                .Text("Data: 10 Aprile 2026")
                .Text("Cliente: Mario Rossi — P.IVA IT98765432101")
                .Spacer(5)
                .HorizontalRule()
                .Spacer(3)
                .Table(t => t
                    .Column(width: 90)
                    .Column(width: 20)
                    .Column(width: 25)
                    .Column(width: 25)
                    .HeaderRow("Descrizione", "Qtà", "Prezzo", "Totale")
                    .Row("Licenza Pragmatic Enterprise", "1", "€4.000", "€4.000")
                    .Row("Supporto Premium Annuale", "1", "€1.200", "€1.200")
                    .Row("Formazione Team (2gg)", "1", "€800", "€800"))
                .Spacer(3)
                .HorizontalRule()
                .Text("Subtotale: €6.000,00")
                .Text("IVA 22%: €1.320,00")
                .Heading("TOTALE: €7.320,00", level: 3)
                .Spacer(5)
                .Text("IBAN: IT60 X054 2811 1010 0000 0123 456")
                .Spacer(5)
                .Hyperlink("https://pay.pragmatic.design/invoice/2026-042", "Paga online")
                .Footnote("Tutti i prezzi sono IVA inclusa ai sensi dell'art. 21 DPR 633/72.")
                .Footer(new ParagraphNode
                {
                    Children = [
                        new TextNode { Content = "Pagina " },
                        new FieldNode { FieldType = FieldType.Page },
                        new TextNode { Content = " di " },
                        new FieldNode { FieldType = FieldType.NumPages }
                    ],
                    Style = new NodeStyle { TextAlign = TextAlign.Center, FontSize = 9, Color = "#999999" }
                })
            )
            .Build();

        var docx = DocxRenderer.Render(model);

        docx.Should().NotBeEmpty();
        docx.Length.Should().BeGreaterThan(2000);
        SaveDocx(docx, "invoice-complete.docx");
    }

    [Fact]
    public void Output_DocumentWithToc()
    {
        var model = new DocumentBuilder()
            .Title("Pragmatic User Manual")
            .Author("Pragmatic S.r.l.")
            .Page(p => p
                .Toc(maxLevel: 3, title: "Contents")
                .PageBreak()
                .Heading("Introduction", level: 1)
                .Text("Welcome to the Pragmatic Design user manual.")
                .Text("This document covers all the main features of the framework.")
                .Footnote("For the complete documentation, visit pragmatic.design/docs.")
                .Spacer(5)
                .Heading("Installation", level: 1)
                .Heading("Requirements", level: 2)
                .Text("- .NET 10 SDK")
                .Text("- PostgreSQL 16+")
                .Text("- Node.js 22+ (for the frontend)")
                .Heading("Setup", level: 2)
                .Text("Run the following command to create a new project:")
                .Text("dotnet new pragmatic -n MyApp")
                .Spacer(5)
                .Heading("Architecture", level: 1)
                .Heading("Building Blocks", level: 2)
                .Text("Building blocks are the fundamental components of the framework.")
                .Heading("Source Generator", level: 3)
                .Text("The source generator analyses the code at compile time and generates the necessary files automatically.")
                .Heading("Medium Blocks", level: 2)
                .Text("Medium blocks are reusable packages that implement cross-cutting features.")
                .Spacer(5)
                .Heading("Conclusion", level: 1)
                .Text("For support, contact ")
                .Hyperlink("mailto:support@pragmatic.design", "support@pragmatic.design")
            )
            .Build();

        // Version 1: with updateFields (Word auto-updates on open, shows prompt)
        var docx = DocxRenderer.Render(model);
        docx.Should().NotBeEmpty();
        SaveDocx(docx, "manual-with-toc.docx");

        // Version 2: without updateFields (static TOC with estimated page numbers, no prompt)
        var docxStatic = DocxRenderer.Render(model, options: new DocxRenderOptions { UpdateFieldsOnOpen = false });
        docxStatic.Should().NotBeEmpty();
        SaveDocx(docxStatic, "manual-with-toc-static.docx");
    }

    [Fact]
    public void Output_MultiSectionDocument()
    {
        var model = new DocumentModel
        {
            Title = "Multi-Section Report",
            Author = "Pragmatic",
            Pages = [
                new DocumentPage
                {
                    DifferentFirstPage = true,
                    FirstPageHeader = [new TextNode
                    {
                        Content = "COVER",
                        Style = new NodeStyle { FontSize = 14, FontWeight = FontWeight.Bold, TextAlign = TextAlign.Center }
                    }],
                    Header = [new TextNode { Content = "Report Q1 2026 — Pragmatic S.r.l." }],
                    Footer = [new ParagraphNode
                    {
                        Children = [
                            new TextNode { Content = "Page " },
                            new FieldNode { FieldType = FieldType.Page }
                        ],
                        Style = new NodeStyle { TextAlign = TextAlign.Right }
                    }],
                    Content = [
                        new SpacerNode { Height = 50 },
                        new HeadingNode { Content = "Quarterly Report Q1 2026", Level = 1 },
                        new TextNode { Content = "Pragmatic S.r.l." },
                        new TextNode { Content = "10 April 2026" },
                        new PageBreakNode(),
                        new HeadingNode { Content = "Executive Summary", Level = 1 },
                        new TextNode { Content = "The first quarter of 2026 saw 35% growth over Q4 2025." },
                        new ListNode
                        {
                            Ordered = false,
                            Items = [
                                new ListItem { Content = [new TextNode { Content = "Revenue: €2.4M (+35%)" }] },
                                new ListItem { Content = [new TextNode { Content = "New customers: 47" }] },
                                new ListItem
                                {
                                    Content = [new TextNode { Content = "Team: 28 people" }],
                                    SubList = new ListNode
                                    {
                                        Ordered = false,
                                        Items = [
                                            new ListItem { Content = [new TextNode { Content = "Engineering: 18" }] },
                                            new ListItem { Content = [new TextNode { Content = "Sales: 6" }] },
                                            new ListItem { Content = [new TextNode { Content = "Operations: 4" }] }
                                        ]
                                    }
                                }
                            ]
                        }
                    ]
                },
                new DocumentPage
                {
                    Orientation = PageOrientation.Landscape,
                    Header = [new TextNode { Content = "Report Q1 2026 — Detail" }],
                    Content = [
                        new HeadingNode { Content = "Revenue Detail by Product", Level = 2 },
                        new TableNode
                        {
                            Columns = [
                                new TableColumn { Width = 80 },
                                new TableColumn { Width = 40 },
                                new TableColumn { Width = 40 },
                                new TableColumn { Width = 40 },
                                new TableColumn { Width = 40 }
                            ],
                            Header = MakeRow("Product", "Jan", "Feb", "Mar", "Total"),
                            Rows = [
                                MakeRow("Enterprise", "€320K", "€350K", "€380K", "€1.05M"),
                                MakeRow("Professional", "€180K", "€200K", "€210K", "€590K"),
                                MakeRow("Starter", "€80K", "€90K", "€85K", "€255K")
                            ],
                            RepeatHeader = true
                        }
                    ]
                }
            ]
        };

        var docx = DocxRenderer.Render(model);

        docx.Should().NotBeEmpty();
        SaveDocx(docx, "report-multi-section.docx");
    }
}

using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.E2E.Tests;

/// <summary>
/// Full pipeline E2E: DocumentTemplate + DataContext → Resolve → DocumentModel (snapshot).
/// No renderer yet (PDF/DOCX are future) — validates the model is correct.
/// </summary>
public class DocumentPipelineTests
{
    [Fact]
    public async Task Invoice_FullPipeline_ScalarBinding_Table_Conditional_Translation_Pipes()
    {
        // --- Partials ---
        var companyHeader = new DocumentPartialTemplate
        {
            Name = "company-header",
            Content =
            [
                new ImageTemplate { Source = "{{company.logoUrl}}", Alt = "{{company.name}}", Width = 60 },
                new TextTemplate { Content = "{{company.name}}" },
                new TextTemplate { Content = "{{company.address}}" }
            ]
        };

        var legalFooter = new DocumentPartialTemplate
        {
            Name = "legal-footer",
            Content = [new TextTemplate { Content = "{{t:invoice.legal_notice}}" }]
        };

        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new DocumentTemplateResolver(pipes)
            .WithPartial("company-header", companyHeader)
            .WithPartial("legal-footer", legalFooter);

        // --- Template ---
        var template = new DocumentTemplate
        {
            Title = "{{t:invoice.title}} #{{invoice.number}}",
            Author = "{{company.name}}",
            Language = "it-IT",
            PageSize = PageSize.A4,
            Pages = [new DocumentPageTemplate
            {
                Header = [new PartialTemplate { Name = "company-header" }],
                Footer = [new PartialTemplate { Name = "legal-footer" }],
                Content =
                [
                    new HeadingTemplate { Content = "{{t:invoice.title}} #{{invoice.number}}", Level = 1 },
                    new SpacerTemplate { Height = 5 },
                    new TextTemplate { Content = "{{t:invoice.customer}}: {{customer.fullName}}" },
                    new TextTemplate { Content = "{{t:invoice.date}}: {{invoice.date | date:\"dd/MM/yyyy\"}}" },
                    new TextTemplate { Content = "{{t:invoice.vat_id}}: {{customer.vatId ?? \"N/A\"}}" },
                    new HorizontalRuleTemplate(),
                    // Data-bound table
                    new TableTemplate
                    {
                        Columns = [new TableColumn { Width = 100 }, new TableColumn { Width = 20 }, new TableColumn { Width = 40 }],
                        Header = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{t:table.description}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{t:table.qty}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{t:table.amount}}" }] }
                            ]
                        },
                        DataSource = "invoice.items",
                        RowTemplate = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.description}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.qty}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.total | currency:\"EUR\"}}" }] }
                            ]
                        }
                    },
                    new SpacerTemplate { Height = 3 },
                    // Subtotal + discount conditional + total
                    new TextTemplate { Content = "{{t:invoice.subtotal}}: {{invoice.subtotal | currency:\"EUR\"}}" },
                    new ContainerTemplate
                    {
                        Directives = new() { If = "invoice.hasDiscount" },
                        Children =
                        [
                            new TextTemplate { Content = "{{t:invoice.discount}}: -{{invoice.discount | currency:\"EUR\"}}" }
                        ]
                    },
                    new TextTemplate { Content = "{{t:invoice.total}}: {{invoice.total | currency:\"EUR\"}}" },
                    new SpacerTemplate { Height = 10 },
                    // QR code for payment
                    new BarcodeTemplate { Value = "{{invoice.paymentUrl}}", Type = BarcodeType.QrCode, Width = 30, Height = 30 },
                    new TextTemplate { Content = "{{t:invoice.payment_instructions}}" }
                ]
            }]
        };

        // --- Data ---
        var localizer = new TestLocalizer("it-IT", new Dictionary<string, string>
        {
            ["invoice.title"] = "Fattura",
            ["invoice.customer"] = "Cliente",
            ["invoice.date"] = "Data",
            ["invoice.vat_id"] = "P.IVA",
            ["invoice.legal_notice"] = "Documento fiscale ai sensi dell'art. 21 DPR 633/72",
            ["table.description"] = "Descrizione",
            ["table.qty"] = "Qtà",
            ["table.amount"] = "Importo",
            ["invoice.subtotal"] = "Subtotale",
            ["invoice.discount"] = "Sconto",
            ["invoice.total"] = "Totale",
            ["invoice.payment_instructions"] = "Scansiona il QR code per pagare"
        });

        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .WithLocalizer(localizer)
            .AddSource("company", new Dictionary<string, object?>
            {
                ["name"] = "Pragmatic S.r.l.",
                ["logoUrl"] = "https://cdn.pragmatic.design/logo.png",
                ["address"] = "Via Roma 42, 20121 Milano — P.IVA IT12345678901"
            })
            .AddSource("customer", new Dictionary<string, object?>
            {
                ["fullName"] = "Mario Rossi",
                ["vatId"] = "IT98765432101"
            })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = "2026-042",
                ["date"] = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.FromHours(2)),
                ["hasDiscount"] = true,
                ["subtotal"] = 540.0,
                ["discount"] = 50.0,
                ["total"] = 490.0,
                ["paymentUrl"] = "https://pay.pragmatic.design/inv/2026-042",
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["description"] = "Camera Deluxe (3 notti)", ["qty"] = "1", ["total"] = 450.0 },
                    new() { ["description"] = "Colazione buffet (3gg)", ["qty"] = "3", ["total"] = 90.0 }
                }
            });

        // --- Resolve ---
        var model = await resolver.ResolveAsync(template, ctx);

        // --- Verify resolved model ---
        model.Title.Should().Be("Fattura #2026-042");
        model.Author.Should().Be("Pragmatic S.r.l.");
        model.Language.Should().Be("it-IT");
        model.PageSize.Should().Be(PageSize.A4);

        var page = model.Pages[0];

        // Header (from partial: 3 nodes)
        page.Header.Should().HaveCount(3);
        page.Header![0].Should().BeOfType<ImageNode>().Which.Source.Should().Contain("logo.png");
        page.Header[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Pragmatic S.r.l.");

        // Footer (from partial: 1 node)
        page.Footer.Should().HaveCount(1);
        page.Footer![0].Should().BeOfType<TextNode>().Which.Content.Should().Contain("art. 21 DPR 633/72");

        // Content
        var heading = page.Content[0] as HeadingNode;
        heading!.Content.Should().Be("Fattura #2026-042");

        // Customer + date + VAT
        page.Content.OfType<TextNode>().Should().Contain(t => t.Content == "Cliente: Mario Rossi");
        page.Content.OfType<TextNode>().Should().Contain(t => t.Content == "Data: 09/04/2026");
        page.Content.OfType<TextNode>().Should().Contain(t => t.Content == "P.IVA: IT98765432101");

        // Table
        var table = page.Content.OfType<TableNode>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Camera Deluxe (3 notti)");
        table.Rows[1].Cells[2].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Contain("€").And.Contain("90");

        // Discount visible (hasDiscount=true)
        page.Content.OfType<ContainerNode>().Should().HaveCount(1);
        var discountContainer = page.Content.OfType<ContainerNode>().Single();
        discountContainer.Children[0].Should().BeOfType<TextNode>().Which.Content.Should().Contain("Sconto").And.Contain("€").And.Contain("50");

        // Total
        page.Content.OfType<TextNode>().Should().Contain(t => t.Content.Contains("Totale") && t.Content.Contains("490"));

        // Barcode
        page.Content.OfType<BarcodeNode>().Single().Value.Should().Be("https://pay.pragmatic.design/inv/2026-042");

        // --- Snapshot: serialize to JSON and back ---
        var json = DocumentSerializer.Serialize(model);
        json.Should().NotBeNullOrEmpty();
        var restored = DocumentSerializer.Deserialize(json);
        restored!.Title.Should().Be("Fattura #2026-042");
        restored.Pages[0].Content.OfType<TableNode>().Single().Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task BatchReport_OnePagePerEmployee()
    {
        var resolver = new DocumentTemplateResolver(PipeRegistry.Default.WithI18N());

        var template = new DocumentTemplate
        {
            Title = "Cedolini Aprile 2026",
            PageDataSource = "employees",
            PageItemName = "emp",
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new HeadingTemplate { Content = "Cedolino — {{emp.name}}", Level = 1 },
                    new TextTemplate { Content = "Reparto: {{emp.department}}" },
                    new TextTemplate { Content = "Lordo: {{emp.gross | currency:\"EUR\"}}" },
                    new TextTemplate { Content = "Netto: {{emp.net | currency:\"EUR\"}}" }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("employees", new List<Dictionary<string, object?>>
            {
                new() { ["name"] = "Alice Bianchi", ["department"] = "Engineering", ["gross"] = 3500.0, ["net"] = 2450.0 },
                new() { ["name"] = "Bob Verdi", ["department"] = "Sales", ["gross"] = 3200.0, ["net"] = 2240.0 },
                new() { ["name"] = "Carol Neri", ["department"] = "HR", ["gross"] = 3000.0, ["net"] = 2100.0 }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        model.Title.Should().Be("Cedolini Aprile 2026");
        model.Pages.Should().HaveCount(3);

        model.Pages[0].Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Cedolino — Alice Bianchi");
        model.Pages[1].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Reparto: Sales");
        model.Pages[2].Content[2].Should().BeOfType<TextNode>().Which.Content.Should().Contain("€").And.Contain("3.000");

        // Verify model serialization roundtrip
        var json = DocumentSerializer.Serialize(model);
        var restored = DocumentSerializer.Deserialize(json);
        restored!.Pages.Should().HaveCount(3);
    }

    [Fact]
    public async Task Document_NoDiscount_ConditionalSkipped()
    {
        var resolver = new DocumentTemplateResolver();

        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new TextTemplate { Content = "Subtotal: {{invoice.subtotal}}" },
                    new TextTemplate
                    {
                        Content = "Discount: {{invoice.discount}}",
                        Directives = new() { If = "invoice.hasDiscount" }
                    },
                    new TextTemplate { Content = "Total: {{invoice.total}}" }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["subtotal"] = "€500", ["discount"] = "€0", ["total"] = "€500", ["hasDiscount"] = false
            });

        var model = await resolver.ResolveAsync(template, ctx);

        // Discount line excluded
        model.Pages[0].Content.Should().HaveCount(2);
        model.Pages[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Subtotal: €500");
        model.Pages[0].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Total: €500");
    }

    [Fact]
    public async Task Document_AsyncDataSource_ResolvesLazily()
    {
        var resolver = new DocumentTemplateResolver();

        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content = [new TextTemplate { Content = "Report: {{report.title}} ({{report.rows}} rows)" }]
            }]
        };

        var fetchCount = 0;
        var ctx = new TemplateDataContext()
            .AddSource("report", async ct =>
            {
                fetchCount++;
                await Task.Delay(1, ct);
                return (object?)new Dictionary<string, object?> { ["title"] = "Q1 Sales", ["rows"] = 1500 };
            });

        var model = await resolver.ResolveAsync(template, ctx);

        model.Pages[0].Content[0].Should().BeOfType<TextNode>()
            .Which.Content.Should().Be("Report: Q1 Sales (1500 rows)");
        fetchCount.Should().Be(1, "async source should be fetched exactly once");
    }

    [Fact]
    public async Task Document_ForEachNode_ExpandsMultipleNodes()
    {
        var resolver = new DocumentTemplateResolver();

        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new HeadingTemplate { Content = "Notifications", Level = 2 },
                    new ForEachTemplate
                    {
                        DataSource = "notifications",
                        ItemName = "n",
                        Children =
                        [
                            new TextTemplate { Content = "[{{n.date}}] {{n.message}}" },
                            new SpacerTemplate { Height = 2 }
                        ]
                    }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("notifications", new List<Dictionary<string, object?>>
            {
                new() { ["date"] = "2026-04-09", ["message"] = "Order confirmed" },
                new() { ["date"] = "2026-04-10", ["message"] = "Payment received" }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        // Heading + (2 notifications × 2 nodes each) = 5
        model.Pages[0].Content.Should().HaveCount(5);
        model.Pages[0].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("[2026-04-09] Order confirmed");
        model.Pages[0].Content[3].Should().BeOfType<TextNode>().Which.Content.Should().Be("[2026-04-10] Payment received");
    }
}

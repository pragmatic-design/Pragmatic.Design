using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.Templates.Tests;

public class DocumentTemplateResolverTests
{
    private readonly DocumentTemplateResolver _resolver = new();

    // --- Simple text binding ---

    [Fact]
    public async Task Resolve_TextWithExpression_InterpolatesValue()
    {
        var template = new DocumentTemplate
        {
            Title = "Invoice {{invoice.number}}",
            Pages = [new DocumentPageTemplate
            {
                Content = [new TextTemplate { Content = "Customer: {{customer.name}}" }]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("invoice", new Dictionary<string, object?> { ["number"] = "2026-001" })
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario Rossi" });

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Title.Should().Be("Invoice 2026-001");
        model.Pages[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Customer: Mario Rossi");
    }

    // --- Conditional ($if) ---

    [Fact]
    public async Task Resolve_IfTrue_IncludesNode()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new TextTemplate { Content = "Always visible" },
                    new TextTemplate
                    {
                        Content = "Discount: {{discount}}",
                        Directives = new() { If = "hasDiscount" }
                    }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("hasDiscount", true)
            .AddSource("discount", "20%");

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages[0].Content.Should().HaveCount(2);
        model.Pages[0].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Discount: 20%");
    }

    [Fact]
    public async Task Resolve_IfFalse_ExcludesNode()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new TextTemplate { Content = "Always visible" },
                    new TextTemplate
                    {
                        Content = "Discount section",
                        Directives = new() { If = "hasDiscount" }
                    }
                ]
            }]
        };

        var ctx = new TemplateDataContext().AddSource("hasDiscount", false);

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages[0].Content.Should().HaveCount(1);
    }

    // --- Data-bound table ---

    [Fact]
    public async Task Resolve_TableWithDataSource_ExpandsRows()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new TableTemplate
                    {
                        Columns = [new TableColumn { Width = 100 }, new TableColumn { Width = 50 }],
                        Header = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "Item" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "Price" }] }
                            ]
                        },
                        DataSource = "items",
                        RowTemplate = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.name}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.price}}" }] }
                            ]
                        }
                    }
                ]
            }]
        };

        var items = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Widget", ["price"] = "€99" },
            new() { ["name"] = "Gadget", ["price"] = "€49" },
            new() { ["name"] = "Doohickey", ["price"] = "€25" }
        };
        var ctx = new TemplateDataContext().AddSource("items", items);

        var model = await _resolver.ResolveAsync(template, ctx);

        var table = model.Pages[0].Content[0].Should().BeOfType<TableNode>().Subject;
        table.Header.Should().NotBeNull();
        table.Rows.Should().HaveCount(3);

        var firstRow = table.Rows[0];
        firstRow.Cells[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Widget");
        firstRow.Cells[1].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("€99");
    }

    // --- ForEach node ---

    [Fact]
    public async Task Resolve_ForEachNode_ExpandsChildren()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new ForEachTemplate
                    {
                        DataSource = "items",
                        ItemName = "item",
                        Children =
                        [
                            new TextTemplate { Content = "- {{item.name}}" }
                        ]
                    }
                ]
            }]
        };

        var items = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Alpha" },
            new() { ["name"] = "Beta" }
        };
        var ctx = new TemplateDataContext().AddSource("items", items);

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages[0].Content.Should().HaveCount(2);
        model.Pages[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("- Alpha");
        model.Pages[0].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("- Beta");
    }

    // --- Partial templates ---

    [Fact]
    public async Task Resolve_PartialTemplate_InjectsContent()
    {
        var headerPartial = new DocumentPartialTemplate
        {
            Name = "company-header",
            Content =
            [
                new HeadingTemplate { Content = "{{company.name}}", Level = 1 },
                new TextTemplate { Content = "{{company.address}}" }
            ]
        };

        var resolver = new DocumentTemplateResolver().WithPartial("company-header", headerPartial);

        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new PartialTemplate { Name = "company-header" },
                    new HorizontalRuleTemplate(),
                    new TextTemplate { Content = "Invoice content here" }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic S.r.l.", ["address"] = "Milano, IT" });

        var model = await resolver.ResolveAsync(template, ctx);

        // Partial injects 2 nodes, then HR + text = 4 total
        model.Pages[0].Content.Should().HaveCount(4);
        model.Pages[0].Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Pragmatic S.r.l.");
        model.Pages[0].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Milano, IT");
    }

    [Fact]
    public async Task Resolve_MissingPartial_Throws()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate { Content = [new PartialTemplate { Name = "nonexistent" }] }]
        };

        var act = () => _resolver.ResolveAsync(template, new TemplateDataContext()).AsTask();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*nonexistent*not found*");
    }

    // --- Page repetition (batch) ---

    [Fact]
    public async Task Resolve_PageDataSource_CreatesMultiplePages()
    {
        var template = new DocumentTemplate
        {
            Title = "Batch Report",
            PageDataSource = "employees",
            PageItemName = "emp",
            Pages = [new DocumentPageTemplate
            {
                Content =
                [
                    new HeadingTemplate { Content = "Report for {{emp.name}}", Level = 1 },
                    new TextTemplate { Content = "Department: {{emp.dept}}" }
                ]
            }]
        };

        var employees = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Alice", ["dept"] = "Engineering" },
            new() { ["name"] = "Bob", ["dept"] = "Sales" },
            new() { ["name"] = "Carol", ["dept"] = "HR" }
        };
        var ctx = new TemplateDataContext().AddSource("employees", employees);

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages.Should().HaveCount(3);
        model.Pages[0].Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Report for Alice");
        model.Pages[1].Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Report for Bob");
        model.Pages[2].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Department: HR");
    }

    // --- Complex invoice template ---

    [Fact]
    public async Task Resolve_InvoiceTemplate_FullExample()
    {
        var headerPartial = new DocumentPartialTemplate
        {
            Name = "header",
            Content = [new TextTemplate { Content = "{{company.name}} — Fattura" }]
        };

        var resolver = new DocumentTemplateResolver().WithPartial("header", headerPartial);

        var template = new DocumentTemplate
        {
            Title = "Fattura {{invoice.number}}",
            Language = "it-IT",
            Pages = [new DocumentPageTemplate
            {
                Header = [new PartialTemplate { Name = "header" }],
                Content =
                [
                    new HeadingTemplate { Content = "Fattura #{{invoice.number}}", Level = 1 },
                    new TextTemplate { Content = "Cliente: {{customer.fullName}}" },
                    new TextTemplate { Content = "Data: {{invoice.date}}" },
                    new SpacerTemplate { Height = 5 },
                    new TableTemplate
                    {
                        Columns = [new TableColumn { Width = 100 }, new TableColumn { Width = 40 }],
                        Header = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "Descrizione" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "Importo" }] }
                            ]
                        },
                        DataSource = "invoice.items",
                        RowTemplate = new TableRowTemplate
                        {
                            Cells =
                            [
                                new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.desc}}" }] },
                                new TableCellTemplate { Content = [new TextTemplate { Content = "€{{item.amount}}" }] }
                            ]
                        }
                    },
                    new TextTemplate
                    {
                        Content = "Sconto applicato",
                        Directives = new() { If = "invoice.hasDiscount" }
                    },
                    new BarcodeTemplate { Value = "{{invoice.paymentUrl}}", Type = BarcodeType.QrCode }
                ]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic S.r.l." })
            .AddSource("customer", new Dictionary<string, object?> { ["fullName"] = "Mario Rossi" })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = "2026-042",
                ["date"] = "09/04/2026",
                ["hasDiscount"] = false,
                ["paymentUrl"] = "https://pay.example.com/inv/2026-042",
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["desc"] = "Licenza Enterprise", ["amount"] = "5000" },
                    new() { ["desc"] = "Supporto annuale", ["amount"] = "1200" }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        model.Title.Should().Be("Fattura 2026-042");
        model.Language.Should().Be("it-IT");

        var page = model.Pages[0];
        // Header from partial
        page.Header.Should().HaveCount(1);
        page.Header![0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Pragmatic S.r.l. — Fattura");

        // Heading
        page.Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Fattura #2026-042");
        // Customer
        page.Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Cliente: Mario Rossi");
        // Table
        var table = page.Content[4].Should().BeOfType<TableNode>().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Licenza Enterprise");

        // Discount section skipped (hasDiscount=false)
        page.Content.OfType<TextNode>().Should().NotContain(t => t.Content == "Sconto applicato");

        // Barcode
        page.Content.OfType<BarcodeNode>().Should().Contain(bc => bc.Value.Contains("2026-042"));
    }

    // --- Header/Footer resolution ---

    [Fact]
    public async Task Resolve_HeaderAndFooter_WithExpressions()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Header = [new TextTemplate { Content = "Header: {{company.name}}" }],
                Footer = [new TextTemplate { Content = "Page 1" }],
                Content = [new TextTemplate { Content = "Body" }]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Acme" });

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages[0].Header![0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Header: Acme");
        model.Pages[0].Footer![0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Page 1");
    }

    // --- Image/Barcode dynamic binding ---

    [Fact]
    public async Task Resolve_ImageWithDynamicSource()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content = [new ImageTemplate { Source = "{{company.logoUrl}}", Alt = "Logo" }]
            }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["logoUrl"] = "https://cdn.example.com/logo.png" });

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Pages[0].Content[0].Should().BeOfType<ImageNode>().Which.Source.Should().Be("https://cdn.example.com/logo.png");
    }

    // --- Empty collection ---

    [Fact]
    public async Task Resolve_EmptyDataSource_ProducesEmptyTable()
    {
        var template = new DocumentTemplate
        {
            Pages = [new DocumentPageTemplate
            {
                Content = [new TableTemplate
                {
                    DataSource = "items",
                    RowTemplate = new TableRowTemplate
                    {
                        Cells = [new TableCellTemplate { Content = [new TextTemplate { Content = "{{item.name}}" }] }]
                    }
                }]
            }]
        };

        var ctx = new TemplateDataContext().AddSource("items", new List<object>());

        var model = await _resolver.ResolveAsync(template, ctx);

        var table = model.Pages[0].Content[0].Should().BeOfType<TableNode>().Subject;
        table.Rows.Should().BeEmpty();
    }
}

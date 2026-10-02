using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templates.Nodes;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.Markup.Tests;

public class PdxDocParserTests
{
    // --- Basic parsing ---

    [Fact]
    public void Parse_MinimalDocument()
    {
        var markup = """
            <document title="Test" lang="it-IT" page-size="A4">
                <page>
                    <text>Hello</text>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);

        template.Title.Should().Be("Test");
        template.Language.Should().Be("it-IT");
        template.PageSize.Should().Be(PageSize.A4);
        template.Pages.Should().HaveCount(1);
        template.Pages[0].Content[0].Should().BeOfType<TextTemplate>()
            .Which.Content.Should().Be("Hello");
    }

    // --- Document attributes ---

    [Fact]
    public void Parse_DocumentAttributes()
    {
        var markup = """
            <document title="Report" author="Pragmatic" lang="en-US"
                      page-size="A3" orientation="landscape" margin="20 10 20 10">
                <page><text>Content</text></page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);

        template.Title.Should().Be("Report");
        template.Author.Should().Be("Pragmatic");
        template.PageSize.Should().Be(PageSize.A3);
        template.Orientation.Should().Be(PageOrientation.Landscape);
        template.Margins.Top.Should().Be(20);
        template.Margins.Right.Should().Be(10);
        template.Margins.Bottom.Should().Be(20);
        template.Margins.Left.Should().Be(10);
    }

    [Fact]
    public void Parse_UniformMargin()
    {
        var markup = """<document margin="15"><page><text>X</text></page></document>""";

        var template = PdxDocParser.Parse(markup);
        template.Margins.Top.Should().Be(15);
        template.Margins.Right.Should().Be(15);
    }

    // --- Header / Footer ---

    [Fact]
    public void Parse_HeaderAndFooter()
    {
        var markup = """
            <document>
                <page>
                    <header><text>Header text</text></header>
                    <heading level="1">Body</heading>
                    <footer><text>Footer text</text></footer>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var page = template.Pages[0];

        page.Header.Should().HaveCount(1);
        page.Header![0].Should().BeOfType<TextTemplate>().Which.Content.Should().Be("Header text");
        page.Footer.Should().HaveCount(1);
        page.Footer![0].Should().BeOfType<TextTemplate>().Which.Content.Should().Be("Footer text");
        page.Content.Should().HaveCount(1);
        page.Content[0].Should().BeOfType<HeadingTemplate>();
    }

    // --- All node types ---

    [Fact]
    public void Parse_AllNodeTypes()
    {
        var markup = """
            <document>
                <page>
                    <text>Plain text</text>
                    <heading level="2">Subtitle</heading>
                    <image src="logo.png" alt="Logo" width="60" height="40" />
                    <hr thickness="0.3" />
                    <spacer height="5" />
                    <pagebreak />
                    <barcode value="ABC-123" type="code128" width="40" height="15" />
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var content = template.Pages[0].Content;

        content[0].Should().BeOfType<TextTemplate>();
        content[1].Should().BeOfType<HeadingTemplate>().Which.Level.Should().Be(2);
        content[2].Should().BeOfType<ImageTemplate>().Which.Width.Should().Be(60);
        content[3].Should().BeOfType<HorizontalRuleTemplate>().Which.Thickness.Should().Be(0.3);
        content[4].Should().BeOfType<SpacerTemplate>().Which.Height.Should().Be(5);
        content[5].Should().BeOfType<PageBreakTemplate>();
        content[6].Should().BeOfType<BarcodeTemplate>().Which.Type.Should().Be(BarcodeType.Code128);
    }

    // --- Table with data-source ---

    [Fact]
    public void Parse_Table_DataSource()
    {
        var markup = """
            <document>
                <page>
                    <table data-source="invoice.items" repeat-header="true">
                        <column width="100">Description</column>
                        <column width="40" align="right">Amount</column>
                        <row-template>
                            <cell>{{item.description}}</cell>
                            <cell>{{item.total}}</cell>
                        </row-template>
                    </table>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var table = template.Pages[0].Content[0].Should().BeOfType<TableTemplate>().Subject;

        table.DataSource.Should().Be("invoice.items");
        table.RepeatHeader.Should().BeTrue();
        table.Columns.Should().HaveCount(2);
        table.Columns[0].Width.Should().Be(100);
        table.Columns[1].Align.Should().Be(TextAlign.Right);
        table.Header.Should().NotBeNull();
        table.Header!.Cells[0].Content[0].Should().BeOfType<TextTemplate>().Which.Content.Should().Be("Description");
        table.RowTemplate.Should().NotBeNull();
        table.RowTemplate!.Cells[0].Content[0].Should().BeOfType<TextTemplate>().Which.Content.Should().Be("{{item.description}}");
    }

    // --- Static table ---

    [Fact]
    public void Parse_Table_StaticRows()
    {
        var markup = """
            <document>
                <page>
                    <table>
                        <column width="80">Name</column>
                        <column width="40">Value</column>
                        <row>
                            <cell>Subtotal</cell>
                            <cell>€500</cell>
                        </row>
                        <row>
                            <cell>Tax</cell>
                            <cell>€110</cell>
                        </row>
                    </table>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var table = template.Pages[0].Content[0].Should().BeOfType<TableTemplate>().Subject;

        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content[0].Should().BeOfType<TextTemplate>().Which.Content.Should().Be("Subtotal");
    }

    // --- List ---

    [Fact]
    public void Parse_List()
    {
        var markup = """
            <document>
                <page>
                    <list ordered="true">
                        <list-item>First</list-item>
                        <list-item>Second</list-item>
                    </list>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var list = template.Pages[0].Content[0].Should().BeOfType<ListTemplate>().Subject;

        list.Ordered.Should().BeTrue();
        list.Items.Should().HaveCount(2);
    }

    // --- Directives ---

    [Fact]
    public void Parse_IfDirective()
    {
        var markup = """
            <document>
                <page>
                    <container if="order.hasDiscount">
                        <text>Discount applied!</text>
                    </container>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var container = template.Pages[0].Content[0].Should().BeOfType<ContainerTemplate>().Subject;

        container.Directives!.If.Should().Be("order.hasDiscount");
        container.Children.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_ForEach()
    {
        var markup = """
            <document>
                <page>
                    <for-each source="notifications" item="n">
                        <text>[{{n.date}}] {{n.message}}</text>
                    </for-each>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var forEach = template.Pages[0].Content[0].Should().BeOfType<ForEachTemplate>().Subject;

        forEach.DataSource.Should().Be("notifications");
        forEach.ItemName.Should().Be("n");
        forEach.Children.Should().HaveCount(1);
    }

    // --- Partial ---

    [Fact]
    public void Parse_Partial()
    {
        var markup = """
            <document>
                <page>
                    <header><partial name="company-header" /></header>
                    <text>Body</text>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        template.Pages[0].Header![0].Should().BeOfType<PartialTemplate>()
            .Which.Name.Should().Be("company-header");
    }

    // --- Batch (page-data-source) ---

    [Fact]
    public void Parse_BatchPageDataSource()
    {
        var markup = """
            <document title="Cedolini" page-data-source="employees" page-item="emp">
                <page>
                    <heading level="1">Cedolino — {{emp.name}}</heading>
                    <text>Netto: {{emp.net}}</text>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);

        template.PageDataSource.Should().Be("employees");
        template.PageItemName.Should().Be("emp");
        template.Pages.Should().HaveCount(1);
    }

    // --- E2E: markup → parse → resolve → verify model ---

    [Fact]
    public async Task E2E_Invoice_MarkupToModel()
    {
        var markup = """
            <document title="Fattura #{{invoice.number}}" author="{{company.name}}" lang="it-IT"
                      page-size="A4" margin="25 15 25 15">
                <page>
                    <header>
                        <text>{{company.name}} — Fattura</text>
                    </header>

                    <heading level="1">Fattura #{{invoice.number}}</heading>
                    <text>Cliente: {{customer.fullName}}</text>
                    <text>Data: {{invoice.date | date:"dd/MM/yyyy"}}</text>
                    <hr />

                    <table data-source="invoice.items">
                        <column width="100">Descrizione</column>
                        <column width="40" align="right">Importo</column>
                        <row-template>
                            <cell>{{item.desc}}</cell>
                            <cell>{{item.amount | currency:"EUR"}}</cell>
                        </row-template>
                    </table>

                    <container if="invoice.hasDiscount">
                        <text>Sconto: -{{invoice.discount | currency:"EUR"}}</text>
                    </container>

                    <text>Totale: {{invoice.total | currency:"EUR"}}</text>
                    <barcode value="{{invoice.paymentUrl}}" type="qr" width="30" height="30" />

                    <footer>
                        <text>Documento fiscale</text>
                    </footer>
                </page>
            </document>
            """;

        // Parse
        var template = PdxDocParser.Parse(markup);
        template.Title.Should().Be("Fattura #{{invoice.number}}");
        template.PageSize.Should().Be(PageSize.A4);

        // Resolve
        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new DocumentTemplateResolver(pipes);
        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic S.r.l." })
            .AddSource("customer", new Dictionary<string, object?> { ["fullName"] = "Mario Rossi" })
            .AddSource("invoice", new Dictionary<string, object?>
            {
                ["number"] = "2026-042",
                ["date"] = new DateTimeOffset(2026, 4, 9, 0, 0, 0, TimeSpan.FromHours(2)),
                ["hasDiscount"] = true,
                ["discount"] = 50.0,
                ["total"] = 490.0,
                ["paymentUrl"] = "https://pay.example.com/2026-042",
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["desc"] = "Camera Deluxe", ["amount"] = 450.0 },
                    new() { ["desc"] = "Colazione", ["amount"] = 90.0 }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        // Verify
        model.Title.Should().Be("Fattura #2026-042");
        model.Author.Should().Be("Pragmatic S.r.l.");
        model.Language.Should().Be("it-IT");

        var page = model.Pages[0];
        page.Header![0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Pragmatic S.r.l. — Fattura");

        page.Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Fattura #2026-042");
        page.Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Cliente: Mario Rossi");
        page.Content[2].Should().BeOfType<TextNode>().Which.Content.Should().Contain("09/04/2026");

        var table = page.Content.OfType<TableNode>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content[0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Camera Deluxe");

        // Discount visible
        page.Content.OfType<ContainerNode>().Should().HaveCount(1);

        // Barcode resolved
        page.Content.OfType<BarcodeNode>().Single().Value.Should().Be("https://pay.example.com/2026-042");

        page.Footer![0].Should().BeOfType<TextNode>().Which.Content.Should().Be("Documento fiscale");

        // Model roundtrip
        var json = DocumentSerializer.Serialize(model);
        var restored = DocumentSerializer.Deserialize(json);
        restored!.Title.Should().Be("Fattura #2026-042");
    }

    [Fact]
    public async Task E2E_BatchCedolini_MarkupToModel()
    {
        var markup = """
            <document title="Cedolini Aprile 2026" page-data-source="employees" page-item="emp">
                <page>
                    <heading level="1">Cedolino — {{emp.name}}</heading>
                    <text>Reparto: {{emp.dept}}</text>
                    <text>Netto: {{emp.net | currency:"EUR"}}</text>
                </page>
            </document>
            """;

        var template = PdxDocParser.Parse(markup);
        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new DocumentTemplateResolver(pipes);
        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("employees", new List<Dictionary<string, object?>>
            {
                new() { ["name"] = "Alice", ["dept"] = "Engineering", ["net"] = 2450.0 },
                new() { ["name"] = "Bob", ["dept"] = "Sales", ["net"] = 2240.0 }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        model.Pages.Should().HaveCount(2);
        model.Pages[0].Content[0].Should().BeOfType<HeadingNode>().Which.Content.Should().Be("Cedolino — Alice");
        model.Pages[1].Content[1].Should().BeOfType<TextNode>().Which.Content.Should().Be("Reparto: Sales");
    }
}

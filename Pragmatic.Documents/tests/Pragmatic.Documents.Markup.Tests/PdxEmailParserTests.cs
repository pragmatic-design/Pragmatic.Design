using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Documents.Markup;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Email.Model;
using Pragmatic.Email.Templates;
using Pragmatic.Email.Templates.Nodes;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Documents.Markup.Tests;

public class PdxEmailParserTests
{
    // --- Basic parsing ---

    [Fact]
    public void Parse_MinimalEmail()
    {
        var markup = """
            <email subject="Test" lang="en-US">
                <row>
                    <col width="12">
                        <text>Hello World</text>
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        template.Subject.Should().Be("Test");
        template.Language.Should().Be("en-US");
        template.Sections.Should().HaveCount(1);
        template.Sections[0].Columns.Should().HaveCount(1);
        template.Sections[0].Columns[0].Width.Should().Be(1.0);
        template.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailTextTemplate>()
            .Which.Content.Should().Be("Hello World");
    }

    // --- Bootstrap grid ---

    [Fact]
    public void Parse_TwoColumns_BootstrapGrid()
    {
        var markup = """
            <email subject="Grid">
                <row>
                    <col width="7"><text>Left</text></col>
                    <col width="5"><text>Right</text></col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var cols = template.Sections[0].Columns;
        cols.Should().HaveCount(2);
        cols[0].Width.Should().BeApproximately(7.0 / 12, 0.01);
        cols[1].Width.Should().BeApproximately(5.0 / 12, 0.01);
    }

    [Fact]
    public void Parse_ThreeColumns()
    {
        var markup = """
            <email>
                <row>
                    <col width="4"><text>A</text></col>
                    <col width="4"><text>B</text></col>
                    <col width="4"><text>C</text></col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);
        template.Sections[0].Columns.Should().HaveCount(3);
        template.Sections[0].Columns.Should().AllSatisfy(c =>
            c.Width.Should().BeApproximately(1.0 / 3, 0.01));
    }

    // --- Hero ---

    [Fact]
    public void Parse_Hero_CentersContent()
    {
        var markup = """
            <email>
                <hero background="#007bff" padding="40">
                    <heading level="1">Welcome!</heading>
                    <text>Subtitle here</text>
                </hero>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var section = template.Sections[0];
        section.BackgroundColor.Should().Be("#007bff");
        section.Padding.Top.Should().Be(40);

        var content = section.Columns[0].Content;
        content[0].Should().BeOfType<EmailHeadingTemplate>()
            .Which.Align.Should().Be(EmailTextAlign.Center);
        content[1].Should().BeOfType<EmailTextTemplate>()
            .Which.Align.Should().Be(EmailTextAlign.Center);
    }

    // --- Article ---

    [Fact]
    public void Parse_Article_DefaultHeadingLevel2()
    {
        var markup = """
            <email>
                <article>
                    <image src="cover.jpg" alt="Cover" width="560" />
                    <heading>Article Title</heading>
                    <text>Preview text</text>
                    <button href="https://example.com">Read More</button>
                </article>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var content = template.Sections[0].Columns[0].Content;
        content[0].Should().BeOfType<EmailImageTemplate>();
        content[1].Should().BeOfType<EmailHeadingTemplate>().Which.Level.Should().Be(2);
        content[2].Should().BeOfType<EmailTextTemplate>();
        content[3].Should().BeOfType<EmailButtonTemplate>();
    }

    // --- Footer ---

    [Fact]
    public void Parse_Footer_SmallPadding()
    {
        var markup = """
            <email>
                <footer background="#f0f0f0">
                    <text align="center" size="12" color="#999">Legal text</text>
                </footer>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var section = template.Sections[0];
        section.BackgroundColor.Should().Be("#f0f0f0");
        section.Padding.Top.Should().Be(10); // footer default
        section.Columns[0].Content[0].Should().BeOfType<EmailTextTemplate>()
            .Which.FontSize.Should().Be(12);
    }

    // --- Table with data-source ---

    [Fact]
    public void Parse_Table_WithDataSource()
    {
        var markup = """
            <email>
                <row>
                    <col width="12">
                        <table data-source="order.items" border="#eee" padding="10">
                            <column width="200">Item</column>
                            <column width="100" align="right">Price</column>
                            <row-template>
                                <cell>{{item.name}}</cell>
                                <cell>{{item.price}}</cell>
                            </row-template>
                        </table>
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var table = template.Sections[0].Columns[0].Content[0]
            .Should().BeOfType<EmailTableTemplate>().Subject;

        table.DataSource.Should().Be("order.items");
        table.BorderColor.Should().Be("#eee");
        table.CellPadding.Should().Be(10);
        table.Columns.Should().HaveCount(2);
        table.Header.Should().NotBeNull();
        table.Header!.Cells[0].Content.Should().Be("Item");
        table.RowTemplate.Should().NotBeNull();
        table.RowTemplate!.Cells[0].Content.Should().Be("{{item.name}}");
    }

    // --- Directives ---

    [Fact]
    public void Parse_IfDirective()
    {
        var markup = """
            <email>
                <row>
                    <col width="12">
                        <text if="order.hasDiscount">Discount applied!</text>
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        var text = template.Sections[0].Columns[0].Content[0]
            .Should().BeOfType<EmailTextTemplate>().Subject;
        text.Directives.Should().NotBeNull();
        text.Directives!.If.Should().Be("order.hasDiscount");
    }

    [Fact]
    public void Parse_ForDirective_OnRow()
    {
        var markup = """
            <email>
                <row for="product in products">
                    <col width="12">
                        <text>{{product.name}}</text>
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        template.Sections[0].Directives.Should().NotBeNull();
        template.Sections[0].Directives!.For.Should().Be("product in products");
    }

    // --- Partial ---

    [Fact]
    public void Parse_Partial()
    {
        var markup = """
            <email>
                <row>
                    <col width="12">
                        <text>Body</text>
                        <partial name="footer-social" />
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);

        template.Sections[0].Columns[0].Content[1]
            .Should().BeOfType<EmailPartialTemplate>()
            .Which.Name.Should().Be("footer-social");
    }

    // --- All node types ---

    [Fact]
    public void Parse_AllNodeTypes()
    {
        var markup = """
            <email subject="Complete">
                <row>
                    <col width="12">
                        <text>Text</text>
                        <heading level="2">Heading</heading>
                        <image src="img.jpg" alt="Alt" width="200" link="https://link.com" />
                        <button href="https://example.com" background="#28a745">CTA</button>
                        <spacer height="15" />
                        <divider color="#999" thickness="2" />
                    </col>
                </row>
            </email>
            """;

        var template = PdxEmailParser.Parse(markup);
        var content = template.Sections[0].Columns[0].Content;

        content[0].Should().BeOfType<EmailTextTemplate>();
        content[1].Should().BeOfType<EmailHeadingTemplate>().Which.Level.Should().Be(2);
        content[2].Should().BeOfType<EmailImageTemplate>().Which.Link.Should().Be("https://link.com");
        content[3].Should().BeOfType<EmailButtonTemplate>().Which.BackgroundColor.Should().Be("#28a745");
        content[4].Should().BeOfType<EmailSpacerTemplate>().Which.Height.Should().Be(15);
        content[5].Should().BeOfType<EmailDividerTemplate>().Which.Thickness.Should().Be(2);
    }

    // --- E2E: markup → parse → resolve → render HTML ---

    [Fact]
    public async Task E2E_OrderConfirmation_MarkupToHtml()
    {
        var markup = """
            <email subject="Ordine {{order.number}} confermato" preheader="Il tuo ordine è confermato" lang="it-IT">
                <hero background="#1a73e8" padding="30">
                    <heading level="1">Ordine Confermato!</heading>
                    <text color="#ffffff">Grazie {{customer.name}}</text>
                </hero>
                <row>
                    <col width="12">
                        <table data-source="order.items" border="#ddd" padding="8">
                            <column width="300">Articolo</column>
                            <column width="100" align="right">Prezzo</column>
                            <row-template>
                                <cell>{{item.name}}</cell>
                                <cell>{{item.price | currency:"EUR"}}</cell>
                            </row-template>
                        </table>
                        <text if="order.hasDiscount" color="#28a745">Sconto: -{{order.discount | currency:"EUR"}}</text>
                        <button href="https://track.example.com/{{order.id}}">Traccia Ordine</button>
                    </col>
                </row>
                <footer background="#f8f9fa">
                    <text align="center" size="11" color="#999">© 2026 Pragmatic S.r.l.</text>
                </footer>
            </email>
            """;

        // Parse
        var template = PdxEmailParser.Parse(markup);
        template.Subject.Should().Be("Ordine {{order.number}} confermato");
        template.Sections.Should().HaveCount(3);

        // Resolve
        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new EmailTemplateResolver(pipes);
        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario" })
            .AddSource("order", new Dictionary<string, object?>
            {
                ["number"] = "ORD-042",
                ["id"] = "abc123",
                ["hasDiscount"] = true,
                ["discount"] = 50.0,
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["name"] = "Camera Deluxe", ["price"] = 450.0 },
                    new() { ["name"] = "Colazione", ["price"] = 90.0 }
                }
            });

        var model = await resolver.ResolveAsync(template, ctx);

        model.Subject.Should().Be("Ordine ORD-042 confermato");

        // Render HTML
        var renderer = new EmailHtmlRenderer();
        var html = renderer.Render(model);

        html.Should().Contain("Ordine Confermato!");
        html.Should().Contain("Grazie Mario");
        html.Should().Contain("Camera Deluxe");
        html.Should().Contain("€"); // currency formatted (U+20AC is not entity-encoded by WebUtility)
        html.Should().Contain("Sconto"); // discount visible
        html.Should().Contain("track.example.com/abc123");
        html.Should().Contain("&#169; 2026 Pragmatic");
        html.Should().Contain("<v:roundrect"); // VML button
        html.Should().Contain("<!--[if mso]>"); // MSO conditional
    }
}

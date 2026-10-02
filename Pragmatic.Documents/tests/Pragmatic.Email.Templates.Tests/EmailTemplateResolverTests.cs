using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Email.Model;
using Pragmatic.Email.Templates;
using Pragmatic.Email.Templates.Nodes;

namespace Pragmatic.Email.Templates.Tests;

public class EmailTemplateResolverTests
{
    private readonly EmailTemplateResolver _resolver = new();

    // --- Basic text binding ---

    [Fact]
    public async Task Resolve_SubjectWithExpression()
    {
        var template = new EmailTemplate
        {
            Subject = "Order {{order.number}} confirmed",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "Hello {{customer.name}}" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("order", new Dictionary<string, object?> { ["number"] = "ORD-042" })
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario" });

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Subject.Should().Be("Order ORD-042 confirmed");
        model.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailTextNode>()
            .Which.Content.Should().Be("Hello Mario");
    }

    // --- Conditional section ---

    [Fact]
    public async Task Resolve_SectionWithIfTrue_Included()
    {
        var template = new EmailTemplate
        {
            Sections =
            [
                new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [new EmailTextTemplate { Content = "Always" }] }] },
                new EmailSectionTemplate
                {
                    Directives = new() { If = "showPromo" },
                    Columns = [new EmailColumnTemplate { Content = [new EmailTextTemplate { Content = "Promo!" }] }]
                }
            ]
        };

        var ctx = new TemplateDataContext().AddSource("showPromo", true);
        var model = await _resolver.ResolveAsync(template, ctx);

        model.Sections.Should().HaveCount(2);
    }

    [Fact]
    public async Task Resolve_SectionWithIfFalse_Excluded()
    {
        var template = new EmailTemplate
        {
            Sections =
            [
                new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [new EmailTextTemplate { Content = "Always" }] }] },
                new EmailSectionTemplate
                {
                    Directives = new() { If = "showPromo" },
                    Columns = [new EmailColumnTemplate { Content = [new EmailTextTemplate { Content = "Promo!" }] }]
                }
            ]
        };

        var ctx = new TemplateDataContext().AddSource("showPromo", false);
        var model = await _resolver.ResolveAsync(template, ctx);

        model.Sections.Should().HaveCount(1);
    }

    // --- Conditional node ---

    [Fact]
    public async Task Resolve_NodeWithIfFalse_Excluded()
    {
        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "Visible" },
                new EmailTextTemplate { Content = "Hidden", Directives = new() { If = "show" } }
            ] }] }]
        };

        var ctx = new TemplateDataContext().AddSource("show", false);
        var model = await _resolver.ResolveAsync(template, ctx);

        model.Sections[0].Columns[0].Content.Should().HaveCount(1);
    }

    // --- Data-bound table ---

    [Fact]
    public async Task Resolve_EmailTableWithDataSource()
    {
        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTableTemplate
                {
                    Columns = [new EmailTableColumn { Width = 200 }, new EmailTableColumn { Width = 100 }],
                    Header = new EmailTableRowTemplate
                    {
                        Cells = [new EmailTableCellTemplate { Content = "Item", Bold = true }, new EmailTableCellTemplate { Content = "Price", Bold = true }]
                    },
                    DataSource = "items",
                    RowTemplate = new EmailTableRowTemplate
                    {
                        Cells = [new EmailTableCellTemplate { Content = "{{item.name}}" }, new EmailTableCellTemplate { Content = "€{{item.price}}" }]
                    },
                    BorderColor = "#ddd"
                }
            ] }] }]
        };

        var items = new List<Dictionary<string, object?>>
        {
            new() { ["name"] = "Widget", ["price"] = "99" },
            new() { ["name"] = "Gadget", ["price"] = "49" }
        };
        var ctx = new TemplateDataContext().AddSource("items", items);

        var model = await _resolver.ResolveAsync(template, ctx);

        var table = model.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailTableNode>().Subject;
        table.Header!.Cells[0].Content.Should().Be("Item");
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content.Should().Be("Widget");
        table.Rows[1].Cells[1].Content.Should().Be("€49");
    }

    // --- Button binding ---

    [Fact]
    public async Task Resolve_ButtonWithExpressions()
    {
        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailButtonTemplate { Text = "Track {{order.number}}", Href = "https://track.example.com/{{order.id}}" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("order", new Dictionary<string, object?> { ["number"] = "ORD-001", ["id"] = "abc123" });

        var model = await _resolver.ResolveAsync(template, ctx);

        var btn = model.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailButtonNode>().Subject;
        btn.Text.Should().Be("Track ORD-001");
        btn.Href.Should().Be("https://track.example.com/abc123");
    }

    // --- Image binding ---

    [Fact]
    public async Task Resolve_ImageWithDynamicSource()
    {
        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailImageTemplate { Source = "{{product.imageUrl}}", Alt = "{{product.name}}" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("product", new Dictionary<string, object?> { ["imageUrl"] = "https://cdn/img.jpg", ["name"] = "Widget" });

        var model = await _resolver.ResolveAsync(template, ctx);

        var img = model.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailImageNode>().Subject;
        img.Source.Should().Be("https://cdn/img.jpg");
        img.Alt.Should().Be("Widget");
    }

    // --- Partial ---

    [Fact]
    public async Task Resolve_Partial_InjectsContent()
    {
        var footerPartial = new EmailPartialDefinition
        {
            Name = "footer",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "© {{company.name}}", Align = EmailTextAlign.Center }
            ] }] }]
        };

        var resolver = new EmailTemplateResolver().WithPartial("footer", footerPartial);

        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "Body" },
                new EmailPartialTemplate { Name = "footer" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic" });

        var model = await resolver.ResolveAsync(template, ctx);

        var content = model.Sections[0].Columns[0].Content;
        content.Should().HaveCount(2);
        content[1].Should().BeOfType<EmailTextNode>().Which.Content.Should().Be("© Pragmatic");
    }

    // --- Full order confirmation ---

    [Fact]
    public async Task Resolve_OrderConfirmation_FullExample()
    {
        var template = new EmailTemplate
        {
            Subject = "Order #{{order.number}} — Confirmed",
            Preheader = "Your order is on its way",
            Sections =
            [
                new EmailSectionTemplate
                {
                    BackgroundColor = "#007bff",
                    Columns = [new EmailColumnTemplate { Content = [
                        new EmailHeadingTemplate { Content = "Order Confirmed!", Align = EmailTextAlign.Center }
                    ] }]
                },
                new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                    new EmailTextTemplate { Content = "Hi {{customer.name}}," },
                    new EmailTextTemplate { Content = "Your order #{{order.number}} has been confirmed." },
                    new EmailTableTemplate
                    {
                        Columns = [new EmailTableColumn { Width = 300 }, new EmailTableColumn { Width = 100 }],
                        Header = new EmailTableRowTemplate { Cells = [
                            new EmailTableCellTemplate { Content = "Item", Bold = true },
                            new EmailTableCellTemplate { Content = "Total", Bold = true }
                        ] },
                        DataSource = "order.items",
                        RowTemplate = new EmailTableRowTemplate { Cells = [
                            new EmailTableCellTemplate { Content = "{{item.name}}" },
                            new EmailTableCellTemplate { Content = "€{{item.total}}" }
                        ] },
                        BorderColor = "#eeeeee"
                    },
                    new EmailTextTemplate
                    {
                        Content = "Discount applied!",
                        Directives = new() { If = "order.hasDiscount" }
                    },
                    new EmailButtonTemplate { Text = "Track Order", Href = "https://orders.example.com/{{order.id}}" }
                ] }] }
            ]
        };

        var ctx = new TemplateDataContext()
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Mario Rossi" })
            .AddSource("order", new Dictionary<string, object?>
            {
                ["number"] = "ORD-2026-042",
                ["id"] = "abc-123",
                ["hasDiscount"] = false,
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["name"] = "Room Deluxe", ["total"] = "450" },
                    new() { ["name"] = "Breakfast", ["total"] = "90" }
                }
            });

        var model = await _resolver.ResolveAsync(template, ctx);

        model.Subject.Should().Be("Order #ORD-2026-042 — Confirmed");
        model.Sections.Should().HaveCount(2);

        // Hero section
        model.Sections[0].Columns[0].Content[0].Should().BeOfType<EmailHeadingNode>()
            .Which.Content.Should().Be("Order Confirmed!");

        // Body section
        var body = model.Sections[1].Columns[0].Content;
        body[0].Should().BeOfType<EmailTextNode>().Which.Content.Should().Be("Hi Mario Rossi,");

        // Table
        var table = body[2].Should().BeOfType<EmailTableNode>().Subject;
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content.Should().Be("Room Deluxe");

        // Discount hidden (hasDiscount=false) → button is at index 3 not 4
        body[3].Should().BeOfType<EmailButtonNode>().Which.Href.Should().Be("https://orders.example.com/abc-123");
    }

    private static EmailTemplate WrapNode(EmailNodeTemplate node) => new()
    {
        Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [node] }] }],
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_HtmlTemplate_PropagatesIsTrusted(bool trusted)
    {
        var template = WrapNode(new EmailHtmlTemplate { Html = "<b>x</b>", IsTrusted = trusted });

        var model = await _resolver.ResolveAsync(template, new TemplateDataContext());

        model.Sections[0].Columns[0].Content[0]
            .Should().BeOfType<EmailHtmlNode>().Which.IsTrusted.Should().Be(trusted);
    }

    [Fact]
    public async Task Resolve_TableWithDataSourceButNoRowTemplate_Throws()
    {
        var template = WrapNode(new EmailTableTemplate { DataSource = "items" });

        var act = async () => await _resolver.ResolveAsync(template, new TemplateDataContext());

        await act.Should().ThrowAsync<Pragmatic.Documents.Templating.Expressions.TemplateParseException>();
    }

    private sealed record UnknownNodeTemplate : EmailNodeTemplate;

    [Fact]
    public async Task Resolve_UnknownNodeTemplate_ThrowsInsteadOfSilentDrop()
    {
        var template = WrapNode(new UnknownNodeTemplate());

        var act = async () => await _resolver.ResolveAsync(template, new TemplateDataContext());

        await act.Should().ThrowAsync<Pragmatic.Documents.Templating.Expressions.TemplateParseException>();
    }
}

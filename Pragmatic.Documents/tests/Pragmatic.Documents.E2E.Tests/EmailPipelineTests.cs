using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.I18N;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Email.Model;
using Pragmatic.Email.Templates;
using Pragmatic.Email.Templates.Nodes;

namespace Pragmatic.Documents.E2E.Tests;

/// <summary>
/// Full pipeline E2E: EmailTemplate + DataContext → Resolve → EmailModel → Render → HTML.
/// No DB, no HTTP — pure in-process pipeline validation.
/// </summary>
public class EmailPipelineTests
{
    private readonly EmailTemplateResolver _resolver;
    private readonly IEmailRenderer _renderer = new EmailHtmlRenderer();

    public EmailPipelineTests()
    {
        var pipes = PipeRegistry.Default.WithI18N();
        _resolver = new EmailTemplateResolver(pipes);
    }

    [Fact]
    public async Task OrderConfirmation_FullPipeline()
    {
        // --- Template (as if loaded from DB) ---
        var template = new EmailTemplate
        {
            Subject = "Ordine #{{order.number}} confermato",
            Preheader = "{{t:email.order_preheader}}",
            Language = "it-IT",
            WrapperBackgroundColor = "#f4f4f4",
            Sections =
            [
                // Hero
                new EmailSectionTemplate
                {
                    BackgroundColor = "#1a73e8",
                    Padding = EmailPadding.All(30),
                    Columns = [new EmailColumnTemplate { Content = [
                        new EmailImageTemplate { Source = "{{company.logoUrl}}", Alt = "{{company.name}}", Width = 120, Align = EmailTextAlign.Center },
                        new EmailHeadingTemplate { Content = "{{t:email.order_confirmed}}", Level = 1, Align = EmailTextAlign.Center }
                    ] }]
                },
                // Body
                new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                    new EmailTextTemplate { Content = "{{t:email.greeting(name=customer.firstName)}}" },
                    new EmailTextTemplate { Content = "{{t:email.order_intro(number=order.number, date=order.date | date:\"dd/MM/yyyy\")}}" },
                    new EmailSpacerTemplate { Height = 10 },
                    // Items table
                    new EmailTableTemplate
                    {
                        Columns = [new EmailTableColumn { Width = 300 }, new EmailTableColumn { Width = 60 }, new EmailTableColumn { Width = 100 }],
                        Header = new EmailTableRowTemplate { Cells = [
                            new EmailTableCellTemplate { Content = "{{t:table.description}}", Bold = true },
                            new EmailTableCellTemplate { Content = "{{t:table.qty}}", Bold = true },
                            new EmailTableCellTemplate { Content = "{{t:table.amount}}", Bold = true }
                        ] },
                        DataSource = "order.items",
                        RowTemplate = new EmailTableRowTemplate { Cells = [
                            new EmailTableCellTemplate { Content = "{{item.description}}" },
                            new EmailTableCellTemplate { Content = "{{item.qty}}" },
                            new EmailTableCellTemplate { Content = "{{item.total | currency:\"EUR\"}}" }
                        ] },
                        BorderColor = "#e0e0e0",
                        CellPadding = 10
                    },
                    new EmailSpacerTemplate { Height = 5 },
                    new EmailTextTemplate { Content = "{{t:table.total}}: {{order.total | currency:\"EUR\"}}" },
                    // Discount (conditional)
                    new EmailTextTemplate
                    {
                        Content = "{{t:email.discount_applied}}: -{{order.discount | currency:\"EUR\"}}",
                        Directives = new() { If = "order.hasDiscount" }
                    },
                    new EmailSpacerTemplate { Height = 15 },
                    new EmailButtonTemplate { Text = "{{t:email.track_button}}", Href = "https://orders.example.com/{{order.id}}" }
                ] }] },
                // Footer
                new EmailSectionTemplate
                {
                    BackgroundColor = "#f8f9fa",
                    Padding = EmailPadding.All(15),
                    Columns = [new EmailColumnTemplate { Content = [
                        new EmailTextTemplate { Content = "{{company.name}} — {{company.address}}", Align = EmailTextAlign.Center, FontSize = 12, Color = "#999999" }
                    ] }]
                }
            ]
        };

        // --- DataContext (as if from DB queries) ---
        var localizer = new TestLocalizer("it-IT", new Dictionary<string, string>
        {
            ["email.order_preheader"] = "Il tuo ordine è confermato",
            ["email.order_confirmed"] = "Ordine Confermato!",
            ["email.greeting"] = "Ciao {0},",
            ["email.order_intro"] = "Il tuo ordine #{0} del {1} è stato confermato.",
            ["email.discount_applied"] = "Sconto applicato",
            ["email.track_button"] = "Traccia Ordine",
            ["table.description"] = "Descrizione",
            ["table.qty"] = "Qtà",
            ["table.amount"] = "Importo",
            ["table.total"] = "Totale"
        });

        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .WithLocalizer(localizer)
            .AddSource("company", new Dictionary<string, object?>
            {
                ["name"] = "Pragmatic S.r.l.",
                ["logoUrl"] = "https://cdn.pragmatic.design/logo.png",
                ["address"] = "Via Roma 42, 20121 Milano"
            })
            .AddSource("customer", new Dictionary<string, object?>
            {
                ["firstName"] = "Mario",
                ["lastName"] = "Rossi",
                ["email"] = "mario.rossi@example.com"
            })
            .AddSource("order", new Dictionary<string, object?>
            {
                ["id"] = "ord-2026-042",
                ["number"] = "ORD-2026-042",
                ["date"] = new DateTimeOffset(2026, 4, 9, 14, 30, 0, TimeSpan.FromHours(2)),
                ["hasDiscount"] = true,
                ["discount"] = 50.0,
                ["total"] = 490.0,
                ["items"] = new List<Dictionary<string, object?>>
                {
                    new() { ["description"] = "Camera Deluxe (3 notti)", ["qty"] = "1", ["total"] = 450.0 },
                    new() { ["description"] = "Colazione buffet", ["qty"] = "3", ["total"] = 90.0 }
                }
            });

        // --- Resolve ---
        var model = await _resolver.ResolveAsync(template, ctx);

        // --- Verify resolved model ---
        model.Subject.Should().Be("Ordine #ORD-2026-042 confermato");
        model.Preheader.Should().Be("Il tuo ordine è confermato");
        model.Sections.Should().HaveCount(3);

        // Table rows expanded
        var bodyContent = model.Sections[1].Columns[0].Content;
        var table = bodyContent.OfType<EmailTableNode>().Single();
        table.Rows.Should().HaveCount(2);
        table.Rows[0].Cells[0].Content.Should().Be("Camera Deluxe (3 notti)");
        table.Rows[1].Cells[2].Content.Should().Contain("€").And.Contain("90");

        // Discount visible (hasDiscount=true)
        bodyContent.OfType<EmailTextNode>()
            .Should().Contain(t => t.Content.Contains("Sconto applicato"));

        // Total formatted
        bodyContent.OfType<EmailTextNode>()
            .Should().Contain(t => t.Content.Contains("Totale") && t.Content.Contains("€") && t.Content.Contains("490"));

        // Button href resolved
        bodyContent.OfType<EmailButtonNode>().Single().Href
            .Should().Be("https://orders.example.com/ord-2026-042");

        // --- Render HTML ---
        var html = _renderer.Render(model);

        // Structure
        html.Should().Contain("<!DOCTYPE html");
        html.Should().Contain("lang=\"it-IT\"");
        html.Should().Contain("<title>Ordine #ORD-2026-042 confermato</title>");

        // Content
        html.Should().Contain("Ciao Mario,");
        html.Should().Contain("09/04/2026"); // date formatted it-IT
        html.Should().Contain("Camera Deluxe");
        html.Should().Contain("Colazione buffet");
        html.Should().Contain("Traccia Ordine");

        // Email techniques
        html.Should().Contain("<v:roundrect"); // VML button
        html.Should().Contain("<!--[if mso]>"); // MSO conditionals
        html.Should().Contain("role=\"presentation\""); // accessible tables
        html.Should().Contain("border:1px solid #e0e0e0"); // table border

        // Company footer
        html.Should().Contain("Via Roma 42");
    }

    [Fact]
    public async Task WelcomeEmail_WithConditionalPromo()
    {
        var template = new EmailTemplate
        {
            Subject = "Benvenuto {{customer.name}}!",
            Sections =
            [
                new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                    new EmailHeadingTemplate { Content = "{{t:welcome.title}}", Align = EmailTextAlign.Center },
                    new EmailTextTemplate { Content = "{{t:welcome.body(name=customer.name)}}" },
                    // Promo section only for VIP
                    new EmailTextTemplate
                    {
                        Content = "{{t:welcome.vip_promo}}",
                        Directives = new() { If = "customer.isVip" }
                    },
                    new EmailButtonTemplate { Text = "{{t:welcome.cta}}", Href = "https://app.example.com/onboarding" }
                ] }] }
            ]
        };

        var localizer = new TestLocalizer("en-US", new Dictionary<string, string>
        {
            ["welcome.title"] = "Welcome!",
            ["welcome.body"] = "Hi {0}, we're glad you joined.",
            ["welcome.vip_promo"] = "As a VIP member, you get 20% off your first month!",
            ["welcome.cta"] = "Get Started"
        });

        // --- VIP customer ---
        var vipCtx = new TemplateDataContext()
            .WithLocalizer(localizer)
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Alice", ["isVip"] = true });

        var vipModel = await _resolver.ResolveAsync(template, vipCtx);
        var vipHtml = _renderer.Render(vipModel);

        vipHtml.Should().Contain("VIP member");
        vipHtml.Should().Contain("20% off");

        // --- Regular customer ---
        var regularCtx = new TemplateDataContext()
            .WithLocalizer(localizer)
            .AddSource("customer", new Dictionary<string, object?> { ["name"] = "Bob", ["isVip"] = false });

        var regularModel = await _resolver.ResolveAsync(template, regularCtx);
        var regularHtml = _renderer.Render(regularModel);

        regularHtml.Should().Contain("Hi Bob");
        regularHtml.Should().NotContain("VIP member");
    }

    [Fact]
    public async Task BatchEmail_SameTemplate_MultipleRecipients()
    {
        var template = new EmailTemplate
        {
            Subject = "Reminder for {{recipient.name}}",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "Dear {{recipient.name}}, your appointment is on {{recipient.date | date:\"dd MMM yyyy\"}}." },
                new EmailButtonTemplate { Text = "Confirm", Href = "https://app.example.com/confirm/{{recipient.id}}" }
            ] }] }]
        };

        var recipients = new[]
        {
            new Dictionary<string, object?> { ["name"] = "Alice", ["date"] = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero), ["id"] = "a1" },
            new Dictionary<string, object?> { ["name"] = "Bob", ["date"] = new DateTimeOffset(2026, 5, 2, 14, 0, 0, TimeSpan.Zero), ["id"] = "b2" },
            new Dictionary<string, object?> { ["name"] = "Carol", ["date"] = new DateTimeOffset(2026, 5, 3, 11, 0, 0, TimeSpan.Zero), ["id"] = "c3" },
        };

        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new EmailTemplateResolver(pipes);
        var results = new List<(EmailModel Model, string Html)>();

        foreach (var recipient in recipients)
        {
            var ctx = new TemplateDataContext()
                .WithCulture("en-US")
                .AddSource("recipient", recipient);

            var model = await resolver.ResolveAsync(template, ctx);
            var html = _renderer.Render(model);
            results.Add((model, html));
        }

        results.Should().HaveCount(3);

        results[0].Model.Subject.Should().Be("Reminder for Alice");
        results[0].Html.Should().Contain("01 May 2026");
        results[0].Html.Should().Contain("confirm/a1");

        results[1].Model.Subject.Should().Be("Reminder for Bob");
        results[1].Html.Should().Contain("02 May 2026");

        results[2].Model.Subject.Should().Be("Reminder for Carol");
        results[2].Html.Should().Contain("confirm/c3");
    }

    [Fact]
    public async Task Email_WithPartial_Footer()
    {
        var footerPartial = new EmailPartialDefinition
        {
            Name = "standard-footer",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailDividerTemplate(),
                new EmailTextTemplate { Content = "© 2026 {{company.name}}", Align = EmailTextAlign.Center, FontSize = 12, Color = "#999" },
                new EmailTextTemplate { Content = "{{company.address}}", Align = EmailTextAlign.Center, FontSize = 11, Color = "#999" }
            ] }] }]
        };

        var resolver = new EmailTemplateResolver().WithPartial("standard-footer", footerPartial);

        var template = new EmailTemplate
        {
            Subject = "Newsletter",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailHeadingTemplate { Content = "Monthly Update" },
                new EmailTextTemplate { Content = "Here's what happened..." },
                new EmailPartialTemplate { Name = "standard-footer" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("company", new Dictionary<string, object?> { ["name"] = "Pragmatic", ["address"] = "Milano, Italy" });

        var model = await resolver.ResolveAsync(template, ctx);
        var html = _renderer.Render(model);

        html.Should().Contain("Monthly Update");
        html.Should().Contain("&#169; 2026 Pragmatic");
        html.Should().Contain("Milano, Italy");
    }

    [Fact]
    public async Task Email_NullCoalescing_AndTernary()
    {
        var template = new EmailTemplate
        {
            Subject = "Status: {{status ?? \"pending\"}}",
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTextTemplate { Content = "Tier: {{amount > 1000 ? \"Premium\" : \"Standard\"}}" },
                new EmailTextTemplate { Content = "Name: {{nickname ?? fullName ?? \"Guest\"}}" }
            ] }] }]
        };

        var ctx = new TemplateDataContext()
            .AddSource("status", (object?)null)
            .AddSource("amount", 1500)
            .AddSource("nickname", (object?)null)
            .AddSource("fullName", "Mario Rossi");

        var model = await _resolver.ResolveAsync(template, ctx);
        var html = _renderer.Render(model);

        model.Subject.Should().Be("Status: pending");
        html.Should().Contain("Tier: Premium");
        html.Should().Contain("Name: Mario Rossi");
    }

    [Fact]
    public async Task Email_Aggregates_InFooter()
    {
        var template = new EmailTemplate
        {
            Sections = [new EmailSectionTemplate { Columns = [new EmailColumnTemplate { Content = [
                new EmailTableTemplate
                {
                    DataSource = "items",
                    RowTemplate = new EmailTableRowTemplate { Cells = [
                        new EmailTableCellTemplate { Content = "{{item.name}}" },
                        new EmailTableCellTemplate { Content = "{{item.price}}" }
                    ] }
                },
                new EmailTextTemplate { Content = "Items: {{items.count}}" },
                new EmailTextTemplate { Content = "Total: {{items.sum(price) | currency:\"EUR\"}}" },
                new EmailTextTemplate { Content = "Average: {{items.avg(price) | currency:\"EUR\"}}" }
            ] }] }]
        };

        var pipes = PipeRegistry.Default.WithI18N();
        var resolver = new EmailTemplateResolver(pipes);
        var ctx = new TemplateDataContext()
            .WithCulture("it-IT")
            .AddSource("items", new List<Dictionary<string, object?>>
            {
                new() { ["name"] = "A", ["price"] = 100.0 },
                new() { ["name"] = "B", ["price"] = 200.0 },
                new() { ["name"] = "C", ["price"] = 300.0 }
            });

        var model = await resolver.ResolveAsync(template, ctx);
        var html = _renderer.Render(model);

        html.Should().Contain("Items: 3");
        html.Should().Contain("€"); // Currency formatted
        // Total = 600, Average = 200
        var textNodes = model.Sections[0].Columns[0].Content.OfType<EmailTextNode>().ToList();
        textNodes.Should().Contain(t => t.Content == "Items: 3");
        textNodes.Should().Contain(t => t.Content.Contains("600"));
        textNodes.Should().Contain(t => t.Content.Contains("200"));
    }
}

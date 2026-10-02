using Pragmatic.Testing.Assertions;
using Pragmatic.Email.Model;

namespace Pragmatic.Email.Model.Tests;

public class EmailBuilderTests
{
    [Fact]
    public void Builder_WelcomeEmail_ProducesValidModel()
    {
        var email = new EmailBuilder()
            .Subject("Welcome to Pragmatic!")
            .Preheader("Start building amazing apps today")
            .Language("en-US")
            .WrapperBackgroundColor("#f4f4f4")
            .FullWidthSection(c => c
                .Heading("Welcome!", level: 1, align: EmailTextAlign.Center)
                .Text("We're excited to have you on board.", align: EmailTextAlign.Center)
            , backgroundColor: "#007bff")
            .FullWidthSection(c => c
                .Text("Here's what you can do:")
                .Spacer(10)
                .Button("Get Started", "https://app.pragmatic.design")
                .Divider()
                .Text("Need help? Reply to this email.", color: "#999999")
            )
            .Build();

        email.Subject.Should().Be("Welcome to Pragmatic!");
        email.Sections.Should().HaveCount(2);

        var hero = email.Sections[0];
        hero.BackgroundColor.Should().Be("#007bff");
        hero.Columns.Should().HaveCount(1);
        hero.Columns[0].Content.Should().HaveCount(2);
        hero.Columns[0].Content[0].Should().BeOfType<EmailHeadingNode>();

        var body = email.Sections[1];
        body.Columns[0].Content.Should().HaveCount(5);
        body.Columns[0].Content[2].Should().BeOfType<EmailButtonNode>();

        // Roundtrip
        var json = EmailSerializer.Serialize(email);
        var deserialized = EmailSerializer.Deserialize(json);
        deserialized!.Subject.Should().Be("Welcome to Pragmatic!");
    }

    [Fact]
    public void Builder_TwoColumnSection_Works()
    {
        var email = new EmailBuilder()
            .Subject("Two Columns")
            .Section(s => s
                .Column(c => c.Text("Left content"), width: 0.5)
                .Column(c => c.Text("Right content"), width: 0.5)
            )
            .Build();

        var section = email.Sections[0];
        section.Columns.Should().HaveCount(2);
        section.Columns[0].Width.Should().Be(0.5);
        section.Columns[1].Width.Should().Be(0.5);
    }

    [Fact]
    public void Builder_OrderConfirmation_ComplexEmail()
    {
        var email = new EmailBuilder()
            .Subject("Order Confirmed — #ORD-2026-042")
            .Preheader("Your order has been confirmed")
            .FullWidthSection(c => c
                .Image("https://cdn.example.com/logo.png", "Company Logo", width: 150)
                .Spacer(20)
                .Heading("Order Confirmed!", level: 2, align: EmailTextAlign.Center)
            , backgroundColor: "#f8f9fa")
            .FullWidthSection(c => c
                .Text("<b>Order #ORD-2026-042</b>")
                .Text("Date: April 9, 2026")
                .Divider()
                .Html("<table width='100%'><tr><td>Widget x2</td><td align='right'>€198.00</td></tr></table>")
                .Divider()
                .Text("<b>Total: €198.00</b>")
            )
            .FullWidthSection(c => c
                .Button("Track Order", "https://orders.example.com/ORD-2026-042")
                .Spacer(20)
                .Text("Questions? Contact support@example.com", align: EmailTextAlign.Center, color: "#999999")
            )
            .Build();

        email.Sections.Should().HaveCount(3);

        // Verify all node types present
        var allNodes = email.Sections.SelectMany(s => s.Columns.SelectMany(c => c.Content)).ToList();
        allNodes.Should().Contain(n => n is EmailImageNode);
        allNodes.Should().Contain(n => n is EmailHeadingNode);
        allNodes.Should().Contain(n => n is EmailButtonNode);
        allNodes.Should().Contain(n => n is EmailDividerNode);
        allNodes.Should().Contain(n => n is EmailHtmlNode);
        allNodes.Should().Contain(n => n is EmailSpacerNode);
    }

    [Fact]
    public void Builder_CustomStyling_Works()
    {
        var email = new EmailBuilder()
            .Width(640)
            .BackgroundColor("#f0f0f0")
            .FontFamily("Georgia, serif")
            .FontSize(18)
            .TextColor("#111111")
            .FullWidthSection(c => c.Text("Styled email"))
            .Build();

        email.Width.Should().Be(640);
        email.BackgroundColor.Should().Be("#f0f0f0");
        email.FontFamily.Should().Be("Georgia, serif");
        email.FontSize.Should().Be(18);
        email.TextColor.Should().Be("#111111");
    }

    [Fact]
    public void Build_ThenMutateBuilder_DoesNotAffectBuiltModel()
    {
        var builder = new EmailBuilder()
            .FullWidthSection(c => c.Text("one"));

        var email = builder.Build();

        builder.FullWidthSection(c => c.Text("two"));

        email.Sections.Should().ContainSingle();
    }
}

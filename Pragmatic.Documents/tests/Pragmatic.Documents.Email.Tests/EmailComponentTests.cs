using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Tests;

/// <summary>
/// Tests for high-level email components (Hero, TwoColumns, Article, Footer, SocialBar).
/// These compile down to standard EmailSection/Column/Node — verify structure and rendering.
/// </summary>
public class EmailComponentTests
{
    private readonly IEmailRenderer _renderer = new EmailHtmlRenderer();

    [Fact]
    public void Hero_ProducesFullWidthSectionWithAllElements()
    {
        var email = new EmailBuilder()
            .Hero(h => h
                .Image("https://cdn.example.com/logo.png", "Logo", width: 150)
                .Heading("Welcome!")
                .Text("We're glad you're here.", color: "#ffffff")
                .Button("Get Started", "https://app.example.com", backgroundColor: "#28a745")
            , backgroundColor: "#007bff")
            .Build();

        email.Sections.Should().HaveCount(1);
        var section = email.Sections[0];
        section.BackgroundColor.Should().Be("#007bff");
        section.Columns.Should().HaveCount(1);

        var content = section.Columns[0].Content;
        content.Should().Contain(n => n is EmailImageNode);
        content.Should().Contain(n => n is EmailHeadingNode);
        content.Should().Contain(n => n is EmailTextNode);
        content.Should().Contain(n => n is EmailButtonNode);
    }

    [Fact]
    public void Hero_Renders_CorrectHtml()
    {
        var email = new EmailBuilder()
            .Hero(h => h
                .Heading("Big Title")
                .Text("Subtitle text")
                .Button("CTA", "https://example.com")
            )
            .Build();

        var html = _renderer.Render(email);

        html.Should().Contain("Big Title");
        html.Should().Contain("Subtitle text");
        html.Should().Contain("https://example.com");
        html.Should().Contain("<v:roundrect"); // bulletproof button
    }

    [Fact]
    public void Hero_WithoutOptionalElements_StillWorks()
    {
        var email = new EmailBuilder()
            .Hero(h => h.Heading("Just a heading"))
            .Build();

        email.Sections.Should().HaveCount(1);
        var content = email.Sections[0].Columns[0].Content;
        content.Should().Contain(n => n is EmailSpacerNode); // spacer before heading
        content.Should().Contain(n => n is EmailHeadingNode);
        content.Should().NotContain(n => n is EmailImageNode);
        content.Should().NotContain(n => n is EmailButtonNode);
    }

    [Fact]
    public void TwoColumns_ProducesTwoColumnSection()
    {
        var email = new EmailBuilder()
            .TwoColumns(
                left: c => c.Text("Left content"),
                right: c => c.Text("Right content")
            )
            .Build();

        email.Sections.Should().HaveCount(1);
        var section = email.Sections[0];
        section.Columns.Should().HaveCount(2);
        section.Columns[0].Width.Should().Be(0.5);
        section.Columns[1].Width.Should().Be(0.5);
    }

    [Fact]
    public void TwoColumns_CustomWidths()
    {
        var email = new EmailBuilder()
            .TwoColumns(
                left: c => c.Image("https://img.example.com/photo.jpg", "Photo", 250),
                right: c => c.Heading("Title").Text("Description"),
                leftWidth: 0.4
            )
            .Build();

        var section = email.Sections[0];
        section.Columns[0].Width.Should().BeApproximately(0.4, 0.001);
        section.Columns[1].Width.Should().BeApproximately(0.6, 0.001);
    }

    [Fact]
    public void TwoColumns_Renders_WithCorrectColumnWidths()
    {
        var email = new EmailBuilder()
            .Width(600)
            .TwoColumns(
                left: c => c.Text("A"),
                right: c => c.Text("B")
            )
            .Build();

        var html = _renderer.Render(email);

        html.Should().Contain("width=\"300\""); // 600 * 0.5
    }

    [Fact]
    public void ThreeColumns_ProducesThreeColumnSection()
    {
        var email = new EmailBuilder()
            .ThreeColumns(
                col1: c => c.Text("Col 1"),
                col2: c => c.Text("Col 2"),
                col3: c => c.Text("Col 3")
            )
            .Build();

        email.Sections[0].Columns.Should().HaveCount(3);
        foreach (var col in email.Sections[0].Columns)
            col.Width.Should().BeApproximately(1.0 / 3, 0.001);
    }

    [Fact]
    public void Article_ProducesImageHeadingTextButton()
    {
        var email = new EmailBuilder()
            .Article(a => a
                .Image("https://cdn.example.com/article.jpg", "Cover", width: 560)
                .Heading("Article Title")
                .Text("This is the article preview...")
                .Button("Read More", "https://blog.example.com/1")
            )
            .Build();

        var content = email.Sections[0].Columns[0].Content;
        content[0].Should().BeOfType<EmailImageNode>().Which.Width.Should().Be(560);
        content[1].Should().BeOfType<EmailHeadingNode>().Which.Level.Should().Be(2); // default H2
        content[2].Should().BeOfType<EmailTextNode>();
        content[3].Should().BeOfType<EmailButtonNode>();
    }

    [Fact]
    public void Footer_HasSmallPadding()
    {
        var email = new EmailBuilder()
            .Footer(c => c
                .Text("© 2026 Pragmatic S.r.l. All rights reserved.", align: EmailTextAlign.Center, color: "#999999")
                .Divider()
                .Text("<a href='https://example.com/unsubscribe'>Unsubscribe</a>", align: EmailTextAlign.Center, color: "#999999")
            , backgroundColor: "#f8f9fa")
            .Build();

        var section = email.Sections[0];
        section.BackgroundColor.Should().Be("#f8f9fa");
        section.Padding.Top.Should().Be(10); // Footer uses 10px padding
        section.Columns[0].Content.Should().HaveCount(3);
    }

    [Fact]
    public void SocialBar_RendersIconLinks()
    {
        var links = new[]
        {
            new SocialLink("https://cdn.example.com/twitter.png", "https://twitter.com/example", "Twitter"),
            new SocialLink("https://cdn.example.com/linkedin.png", "https://linkedin.com/example", "LinkedIn"),
        };

        var email = new EmailBuilder()
            .SocialBar(links)
            .Build();

        var html = _renderer.Render(email);

        html.Should().Contain("https://twitter.com/example");
        html.Should().Contain("alt=\"Twitter\"");
        html.Should().Contain("https://linkedin.com/example");
        html.Should().Contain("alt=\"LinkedIn\"");
        html.Should().Contain("text-align:center");
    }

    [Fact]
    public void FullEmail_WithAllComponents()
    {
        var email = new EmailBuilder()
            .Subject("Monthly Newsletter")
            .Preheader("What's new this month")
            .WrapperBackgroundColor("#f4f4f4")
            .Hero(h => h
                .Image("https://cdn.example.com/logo.png", "Logo", 120)
                .Heading("April Newsletter")
                .Text("Here's what happened this month.")
            , backgroundColor: "#1a1a2e")
            .Article(a => a
                .Image("https://cdn.example.com/feature.jpg", "Feature", 560)
                .Heading("New Feature: Templates")
                .Text("Build beautiful emails without writing HTML.")
                .Button("Learn More", "https://docs.example.com/templates")
            )
            .TwoColumns(
                left: c => c.Heading("Stats", 3).Text("1,000+ users"),
                right: c => c.Heading("Growth", 3).Text("+42% MoM")
            )
            .Footer(c => c
                .Text("© 2026 Pragmatic • <a href='#'>Unsubscribe</a>", align: EmailTextAlign.Center, color: "#999")
            , backgroundColor: "#f0f0f0")
            .Build();

        email.Sections.Should().HaveCount(4); // Hero + Article + TwoColumns + Footer

        var html = _renderer.Render(email);
        html.Should().Contain("April Newsletter");
        html.Should().Contain("New Feature: Templates");
        html.Should().Contain("1,000+ users");
        html.Should().Contain("+42% MoM");
        html.Should().Contain("Unsubscribe");
    }
}

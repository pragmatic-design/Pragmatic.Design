using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Tests;

public class EmailHtmlRendererTests
{
    private readonly IEmailRenderer _renderer = new EmailHtmlRenderer();

    [Fact]
    public void Render_EmptyEmail_ProducesValidHtmlStructure()
    {
        var model = new EmailModel();

        var html = _renderer.Render(model);

        html.Should().Contain("<!DOCTYPE html");
        html.Should().Contain("<html");
        html.Should().Contain("xmlns:v=\"urn:schemas-microsoft-com:vml\"");
        html.Should().Contain("xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        html.Should().Contain("<head>");
        html.Should().Contain("</head>");
        html.Should().Contain("<body");
        html.Should().Contain("</body>");
        html.Should().Contain("</html>");
    }

    [Fact]
    public void Render_WithSubject_SetsTitle()
    {
        var model = new EmailModel { Subject = "Test Email" };

        var html = _renderer.Render(model);

        html.Should().Contain("<title>Test Email</title>");
    }

    [Fact]
    public void Render_WithLanguage_SetsLangAttribute()
    {
        var model = new EmailModel { Language = "it-IT" };

        var html = _renderer.Render(model);

        html.Should().Contain("lang=\"it-IT\"");
    }

    [Fact]
    public void Render_WithPreheader_RendersHiddenDiv()
    {
        var model = new EmailModel { Preheader = "Preview text here" };

        var html = _renderer.Render(model);

        html.Should().Contain("Preview text here");
        html.Should().Contain("display:none");
        html.Should().Contain("max-height:0px");
    }

    [Fact]
    public void Render_ContentTable_HasCorrectWidth()
    {
        var model = new EmailModel { Width = 640 };

        var html = _renderer.Render(model);

        html.Should().Contain("width=\"640\"");
        html.Should().Contain("max-width:640px");
    }

    [Fact]
    public void Render_MsoConditionals_Present()
    {
        var model = new EmailModel();

        var html = _renderer.Render(model);

        html.Should().Contain("<!--[if mso]>");
        html.Should().Contain("<![endif]-->");
        html.Should().Contain("o:PixelsPerInch");
    }

    [Fact]
    public void Render_TextNode_InlineStyles()
    {
        var model = new EmailModel
        {
            FontFamily = "Georgia, serif",
            FontSize = 18,
            TextColor = "#111111",
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [new EmailTextNode { Content = "Hello world" }] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("Hello world");
        html.Should().Contain("font-family:Georgia, serif");
        html.Should().Contain("font-size:18px");
        html.Should().Contain("color:#111111");
    }

    [Fact]
    public void Render_HeadingNode_CorrectTag()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailHeadingNode { Content = "Big Title", Level = 1 },
                new EmailHeadingNode { Content = "Subtitle", Level = 3 }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("<h1");
        html.Should().Contain("Big Title");
        html.Should().Contain("font-size:28px");
        html.Should().Contain("<h3");
        html.Should().Contain("Subtitle");
        html.Should().Contain("font-size:20px");
    }

    [Fact]
    public void Render_ImageNode_WithAttributes()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailImageNode { Source = "https://cdn.example.com/logo.png", Alt = "Logo", Width = 200 }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("src=\"https://cdn.example.com/logo.png\"");
        html.Should().Contain("alt=\"Logo\"");
        html.Should().Contain("width=\"200\"");
        html.Should().Contain("width:200px");
    }

    [Fact]
    public void Render_ImageNode_WithLink()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailImageNode { Source = "https://cdn.example.com/banner.jpg", Alt = "Banner", Link = "https://example.com" }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("<a href=\"https://example.com\"");
        html.Should().Contain("target=\"_blank\"");
    }

    [Fact]
    public void Render_ButtonNode_BulletproofWithVml()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailButtonNode { Text = "Get Started", Href = "https://app.example.com", BackgroundColor = "#28a745" }
            ] }] }]
        };

        var html = _renderer.Render(model);

        // VML for Outlook
        html.Should().Contain("<!--[if mso]>");
        html.Should().Contain("<v:roundrect");
        html.Should().Contain("fillcolor=\"#28a745\"");
        html.Should().Contain("Get Started");

        // Standard HTML for others
        html.Should().Contain("<!--[if !mso]><!-->");
        html.Should().Contain("<a href=\"https://app.example.com\"");
        html.Should().Contain("background-color:#28a745");
        html.Should().Contain("border-radius:");
        html.Should().Contain("<!--<![endif]-->");
    }

    [Fact]
    public void Render_SpacerNode_CorrectHeight()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [new EmailSpacerNode { Height = 30 }] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("height:30px");
        html.Should().Contain("line-height:30px");
    }

    [Fact]
    public void Render_DividerNode_TableBased()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailDividerNode { Color = "#999999", Thickness = 2 }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("border-top:2px solid #999999");
    }

    [Fact]
    public void Render_HtmlNode_PassesThrough()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                // Passthrough is now opt-in (safe by default): trusted markup must be marked explicitly.
                new EmailHtmlNode { Html = "<table><tr><td>Custom</td></tr></table>", IsTrusted = true }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("<table><tr><td>Custom</td></tr></table>");
    }

    [Fact]
    public void Render_TwoColumns_NestedTable()
    {
        var model = new EmailModel
        {
            Width = 600,
            Sections = [new EmailSection
            {
                Columns =
                [
                    new EmailColumn { Width = 0.5, Content = [new EmailTextNode { Content = "Left" }] },
                    new EmailColumn { Width = 0.5, Content = [new EmailTextNode { Content = "Right" }] }
                ]
            }]
        };

        var html = _renderer.Render(model);

        // Two columns produce a nested table with 300px each
        html.Should().Contain("width=\"300\"");
        html.Should().Contain("Left");
        html.Should().Contain("Right");
    }

    [Fact]
    public void Render_ThreeColumns_CorrectWidths()
    {
        var model = new EmailModel
        {
            Width = 600,
            Sections = [new EmailSection
            {
                Columns =
                [
                    new EmailColumn { Width = 0.25, Content = [new EmailTextNode { Content = "A" }] },
                    new EmailColumn { Width = 0.50, Content = [new EmailTextNode { Content = "B" }] },
                    new EmailColumn { Width = 0.25, Content = [new EmailTextNode { Content = "C" }] }
                ]
            }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("width=\"150\""); // 25% of 600
        html.Should().Contain("width=\"300\""); // 50% of 600
    }

    [Fact]
    public void Render_SectionBackground_Applied()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection
            {
                BackgroundColor = "#007bff",
                Columns = [new EmailColumn { Content = [new EmailTextNode { Content = "Blue section" }] }]
            }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("background-color:#007bff");
    }

    [Fact]
    public void Render_SectionPadding_Applied()
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection
            {
                Padding = EmailPadding.All(40),
                Columns = [new EmailColumn { Content = [new EmailTextNode { Content = "Padded" }] }]
            }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("padding:40px 40px 40px 40px");
    }

    [Fact]
    public void Render_CompleteWelcomeEmail_AllElementsPresent()
    {
        var model = new EmailBuilder()
            .Subject("Welcome to Pragmatic!")
            .Preheader("Start building today")
            .Language("en-US")
            .WrapperBackgroundColor("#f4f4f4")
            .FullWidthSection(c => c
                .Image("https://cdn.example.com/logo.png", "Logo", width: 150)
                .Heading("Welcome!", level: 1, align: EmailTextAlign.Center)
                .Text("We're excited to have you on board.", align: EmailTextAlign.Center)
            , backgroundColor: "#007bff")
            .FullWidthSection(c => c
                .Text("Here are your next steps:")
                .Spacer(10)
                .Button("Get Started", "https://app.example.com")
                .Divider()
                .Text("Questions? Reply to this email.", color: "#999999")
            )
            .Build();

        var html = _renderer.Render(model);

        // Structure
        html.Should().Contain("<!DOCTYPE html");
        html.Should().Contain("<title>Welcome to Pragmatic!</title>");
        html.Should().Contain("lang=\"en-US\"");
        html.Should().Contain("Start building today");

        // Content
        html.Should().Contain("Welcome!");
        html.Should().Contain("Get Started");
        html.Should().Contain("https://app.example.com");

        // Techniques
        html.Should().Contain("<v:roundrect"); // VML button
        html.Should().Contain("role=\"presentation\""); // Accessible tables
        html.Should().Contain("border-top:"); // Divider
    }

    [Fact]
    public void RenderTo_TextWriter_SameAsRender()
    {
        var model = new EmailBuilder()
            .Subject("Writer Test")
            .FullWidthSection(c => c.Text("Content"))
            .Build();

        var direct = _renderer.Render(model);

        using var sw = new StringWriter();
        _renderer.RenderTo(sw, model);
        var streamed = sw.ToString();

        streamed.Should().Be(direct);
    }

    [Fact]
    public void Render_HtmlEncodes_SpecialCharacters()
    {
        var model = new EmailModel
        {
            Subject = "Test <script>alert('xss')</script>",
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [
                new EmailHeadingNode { Content = "Price: €50 & <free>" }
            ] }] }]
        };

        var html = _renderer.Render(model);

        html.Should().Contain("&lt;script&gt;");
        html.Should().Contain("&amp;");
        html.Should().Contain("&lt;free&gt;");
        html.Should().NotContain("<script>alert");
    }
}

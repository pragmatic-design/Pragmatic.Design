using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Email;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email.Tests;

/// <summary>
///     S5: the renderer must HTML-encode text by default (consistent with headings/cells) and validate
///     URL schemes, so untrusted data-source content cannot inject script into the email.
/// </summary>
public class EmailHtmlRendererSecurityTests
{
    private readonly IEmailRenderer _renderer = new EmailHtmlRenderer();

    private static string RenderWith(params EmailNode[] nodes)
    {
        var model = new EmailModel
        {
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = nodes }] }],
        };
        return new EmailHtmlRenderer().Render(model);
    }

    [Fact]
    public void Text_ByDefault_IsHtmlEncoded()
    {
        var html = RenderWith(new EmailTextNode { Content = "<script>alert(1)</script>" });

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void Text_WithAllowHtml_IsEmittedVerbatim()
    {
        var html = RenderWith(new EmailTextNode { Content = "Hello <b>world</b>", AllowHtml = true });

        html.Should().Contain("Hello <b>world</b>");
    }

    [Fact]
    public void ImageLink_WithJavascriptScheme_IsDropped()
    {
        var html = RenderWith(new EmailImageNode
        {
            Source = "https://cdn.example.com/x.png",
            Alt = "x",
            Link = "javascript:alert(1)",
        });

        html.Should().NotContain("javascript:alert(1)");
        html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public void ButtonHref_WithJavascriptScheme_IsDropped()
    {
        var html = RenderWith(new EmailButtonNode { Text = "Click", Href = "javascript:alert(1)" });

        html.Should().NotContain("javascript:alert(1)");
        html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public void ImageSource_WithJavascriptScheme_IsDropped()
    {
        var html = RenderWith(new EmailImageNode { Source = "javascript:alert(1)", Alt = "x" });

        html.Should().NotContain("javascript:alert(1)");
        html.Should().Contain("src=\"\"");
    }

    [Fact]
    public void TextLineHeight_WithStyleBreakout_IsRejected()
    {
        var html = RenderWith(new EmailTextNode
        {
            Content = "hi",
            LineHeight = "1.5\"><script>alert(1)</script>",
        });

        html.Should().NotContain("<script>alert(1)</script>");
        // Falls back to the safe default line-height.
        html.Should().Contain("line-height:1.5;");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("24px")]
    [InlineData("normal")]
    public void TextLineHeight_WithValidValue_IsPreserved(string value)
    {
        var html = RenderWith(new EmailTextNode { Content = "hi", LineHeight = value });

        html.Should().Contain($"line-height:{value};");
    }

    [Fact]
    public void FontFamily_WithAttributeBreakout_IsRejected()
    {
        var model = new EmailModel
        {
            FontFamily = "Arial\" onload=\"alert(1)",
            Sections = [new EmailSection { Columns = [new EmailColumn { Content = [new EmailTextNode { Content = "hi" }] }] }],
        };

        var html = new EmailHtmlRenderer().Render(model);

        html.Should().NotContain("onload=");
        // Falls back to the safe default font stack.
        html.Should().Contain("Arial, Helvetica, sans-serif");
    }

    [Fact]
    public void Color_WithAttributeBreakout_IsRejected()
    {
        var html = RenderWith(new EmailTextNode { Content = "hi", Color = "#fff\"><script>alert(1)</script>" });

        html.Should().NotContain("<script>alert(1)</script>");
    }

    [Fact]
    public void HtmlNode_ByDefault_IsHtmlEncoded()
    {
        var html = RenderWith(new EmailHtmlNode { Html = "<script>alert(1)</script>" });

        html.Should().NotContain("<script>alert(1)</script>");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void HtmlNode_WhenExplicitlyTrusted_IsEmittedVerbatim()
    {
        var html = RenderWith(new EmailHtmlNode { Html = "<b>bold</b>", IsTrusted = true });

        html.Should().Contain("<b>bold</b>");
    }
}

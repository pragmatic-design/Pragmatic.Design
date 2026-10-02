using System.Xml.Linq;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// A block's run formatting — font, size, weight, colour and the rest — applies to the text inside it, and
/// a run's own style wins where both set a property, as in CSS.
/// </summary>
/// <remarks>
///     The text is reached from the block's style, not only from a <see cref="TextNode" />'s own: a
///     heading, a paragraph or a link that passed its style to the paragraph properties alone would render
///     <c>new HeadingNode { Style = new NodeStyle { FontSize = 20 } }</c> at the theme's size, and a
///     heading's text has no other place a style could go.
/// </remarks>
public class ABlocksRunFormattingCascadesTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static XElement[] Runs(Action<PageBuilder> page)
    {
        var docx = DocxRenderer.Render(new DocumentBuilder().Page(page).Build());
        return DocxTestHelper.GetPart(docx, "word/document.xml").Descendants(W + "r").ToArray();
    }

    private static string? Value(XElement run, string property)
        => (string?)run.Element(W + "rPr")?.Element(W + property)?.Attribute(W + "val");

    [Fact]
    public void AHeadingsFontSize_ReachesItsText()
    {
        var run = Runs(p => p.Add(new HeadingNode { Content = "Invoice", Style = new NodeStyle { FontSize = 20 } })).Single();

        Value(run, "sz").Should().Be("40", "20 pt is 40 half-points, on the run that carries the heading's text");
    }

    [Fact]
    public void AParagraphsColour_ColoursAChildRunWithoutAStyle()
    {
        var run = Runs(p => p.Add(new ParagraphNode
        {
            Style = new NodeStyle { Color = "C00000" },
            Children = [new TextNode { Content = "Overdue" }]
        })).Single();

        Value(run, "color").Should().Be("C00000");
    }

    [Fact]
    public void AChildsOwnColour_WinsOverTheParagraphs()
    {
        var runs = Runs(p => p.Add(new ParagraphNode
        {
            Style = new NodeStyle { Color = "C00000", FontSize = 9 },
            Children =
            [
                new TextNode { Content = "Own", Style = new NodeStyle { Color = "1F4E79" } },
                new TextNode { Content = "Inherited" }
            ]
        }));

        Value(runs[0], "color").Should().Be("1F4E79", "a run's own style wins where both set the property");
        Value(runs[0], "sz").Should().Be("18", "and inherits the ones it does not set");
        Value(runs[1], "color").Should().Be("C00000");
    }

    [Fact]
    public void ALinksStyle_ReachesItsTextBesideTheHyperlinkStyle()
    {
        var run = Runs(p => p.Add(new HyperlinkNode
        {
            Href = "https://example.com",
            Style = new NodeStyle { FontWeight = FontWeight.Bold },
            Children = [new TextNode { Content = "Pay now" }]
        })).Single();

        run.Element(W + "rPr")!.Element(W + "rStyle")!.Attribute(W + "val")!.Value.Should().Be("Hyperlink");
        run.Element(W + "rPr")!.Element(W + "b").Should().NotBeNull("the link's weight reaches its text");
    }

    /// <summary>The control: a run in an unstyled paragraph is written as before, with no run properties.</summary>
    [Fact]
    public void ARunInAnUnstyledParagraph_IsUnchanged()
    {
        var run = Runs(p => p.Paragraph(new TextNode { Content = "Plain" })).Single();

        run.Element(W + "rPr").Should().BeNull();
    }
}

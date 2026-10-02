using System.Xml.Linq;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Docx.Tests;

/// <summary>
/// <see cref="NodeStyle.LetterSpacing" /> is in millimetres, like every other length in the record. Read
/// as points, <c>LetterSpacing = 1</c> would become 1 pt in Word, where the caller asked for about 2.8.
/// </summary>
public class LetterSpacingIsMillimetresTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The run's <c>w:spacing</c>, in twentieths of a point.</summary>
    private static string? RunSpacing(double letterSpacing)
    {
        var docx = DocxRenderer.Render(new DocumentBuilder()
            .Page(p => p.Text("Spaced", new NodeStyle { LetterSpacing = letterSpacing }))
            .Build());

        return DocxTestHelper.GetPart(docx, "word/document.xml")
            .Descendants(W + "rPr")
            .Elements(W + "spacing")
            .Select(e => (string?)e.Attribute(W + "val"))
            .SingleOrDefault();
    }

    [Fact]
    public void Render_LetterSpacingOneMillimetre_WritesItsTwips()
        => RunSpacing(1).Should().Be("57");

    [Fact]
    public void Render_LetterSpacingHalfMillimetre_WritesItsTwips()
        => RunSpacing(0.5).Should().Be("28");
}

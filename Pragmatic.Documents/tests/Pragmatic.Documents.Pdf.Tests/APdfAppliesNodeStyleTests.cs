using System.Text;
using System.Text.RegularExpressions;
using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
/// The PDF applies <see cref="NodeStyle" /> rather than setting all text in the engine's Noto Sans —
/// which, on a machine without it, means whatever font the search finds first, an emoji face on
/// Windows. What a PDF
/// embeds is readable from its bytes: each font it uses appears as a <c>/BaseFont</c> name, the family
/// without spaces plus the face.
/// </summary>
/// <remarks>
///     The fonts are the machine's. The families here are outside the engine's default list, so the
///     default font cannot satisfy an assertion on its own: Times New Roman and Courier New on Windows,
///     the DejaVu faces elsewhere. On Linux the engine finds them through fontconfig, so the machine
///     needs the <c>fontconfig</c> and <c>fonts-dejavu-core</c> packages.
/// </remarks>
public class APdfAppliesNodeStyleTests
{
    private static readonly string Serif = OperatingSystem.IsWindows() ? "Times New Roman" : "DejaVu Serif";
    private static readonly string Mono = OperatingSystem.IsWindows() ? "Courier New" : "DejaVu Sans Mono";

    private static string BaseFontOf(string family) => family.Replace(" ", "", StringComparison.Ordinal);

    private static string[] EmbeddedFonts(byte[] pdf)
        => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/BaseFont\s*/([^\s/\[\]<>()]+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToArray();

    private static byte[] Render(NodeStyle? style)
        => PdfRenderer.Render(new DocumentBuilder()
            .Page(p => p.Paragraph(new TextNode { Content = "Invoice total", Style = style }))
            .Build());

    [NativeRequiredFact]
    public void Render_TextWithFontFamilyAndBold_EmbedsThatFamilyInBold()
    {
        var fonts = EmbeddedFonts(Render(new NodeStyle { FontFamily = Serif, FontWeight = FontWeight.Bold }));

        fonts.Should().Contain(f => f.Contains(BaseFontOf(Serif)) && f.Contains("Bold"));
    }

    [NativeRequiredFact]
    public void Render_TextWithFontFamily_EmbedsThatFamily()
    {
        var fonts = EmbeddedFonts(Render(new NodeStyle { FontFamily = Serif }));

        fonts.Should().Contain(f => f.Contains(BaseFontOf(Serif)) && !f.Contains("Bold"));
    }

    [NativeRequiredFact]
    public void Render_TextWithNoStyle_UsesATextFaceNotAnEmojiOne()
    {
        var fonts = EmbeddedFonts(Render(null));

        fonts.Should().NotBeEmpty();
        fonts.Should().NotContain(f => f.Contains("Emoji"));
    }

    [NativeRequiredFact]
    public void Render_TextWithAColourThatIsNotHex_Throws()
    {
        var render = () => Render(new NodeStyle { Color = "not-a-colour" });

        render.Should().Throw<PdfRenderException>();
    }

    /// <summary>
    ///     A paragraph's run formatting reaches the text inside it: the family is set on the
    ///     paragraph alone, and the run carries no style of its own.
    /// </summary>
    [NativeRequiredFact]
    public void Render_FontFamilyOnTheParagraphOnly_EmbedsThatFamily()
    {
        var pdf = PdfRenderer.Render(new DocumentBuilder()
            .Page(p => p.Add(new ParagraphNode
            {
                Style = new NodeStyle { FontFamily = Mono },
                Children = [new TextNode { Content = "Invoice total" }]
            }))
            .Build());

        EmbeddedFonts(pdf).Should().Contain(f => f.Contains(BaseFontOf(Mono)));
    }

    /// <summary>And a heading's: its text has no other place a style could go.</summary>
    [NativeRequiredFact]
    public void Render_FontFamilyOnAHeading_EmbedsThatFamily()
    {
        var pdf = PdfRenderer.Render(new DocumentBuilder()
            .Page(p => p.Add(new HeadingNode { Content = "Invoice", Style = new NodeStyle { FontFamily = Mono } }))
            .Build());

        EmbeddedFonts(pdf).Should().Contain(f => f.Contains(BaseFontOf(Mono)));
    }

    /// <summary>
    ///     A heading built from children renders them, not its <c>Content</c>: the family is
    ///     on the child alone, so the family is embedded only if the engine's model keeps
    ///     <c>children</c> on a heading.
    /// </summary>
    [NativeRequiredFact]
    public void Render_AHeadingsChildWithAFontFamily_EmbedsThatFamily()
    {
        var pdf = PdfRenderer.Render(new DocumentBuilder()
            .Page(p => p.Add(new HeadingNode
            {
                Content = "Invoice",
                Children = [new TextNode { Content = "Invoice", Style = new NodeStyle { FontFamily = Mono } }]
            }))
            .Build());

        EmbeddedFonts(pdf).Should().Contain(f => f.Contains(BaseFontOf(Mono)));
    }

    [NativeRequiredFact]
    public void Render_TwoRunsWithDifferentFamilies_EmbedsBoth()
    {
        var pdf = PdfRenderer.Render(new DocumentBuilder()
            .Page(p => p.Paragraph(
                new TextNode { Content = "Serif run", Style = new NodeStyle { FontFamily = Serif } },
                new TextNode { Content = "Mono run", Style = new NodeStyle { FontFamily = Mono } }))
            .Build());

        var fonts = EmbeddedFonts(pdf);
        fonts.Should().Contain(f => f.Contains(BaseFontOf(Serif)));
        fonts.Should().Contain(f => f.Contains(BaseFontOf(Mono)));
    }
}

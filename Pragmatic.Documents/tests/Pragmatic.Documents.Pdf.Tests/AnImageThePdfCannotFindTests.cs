using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
/// The PDF side of the rule the DOCX renderer now follows: an image that resolves to nothing fails
/// the render instead of leaving the page without it. Here it always did — the engine cannot find
/// the file — and this pins it, so the two renderers cannot drift apart unnoticed.
/// </summary>
public class AnImageThePdfCannotFindTests
{
    private static DocumentModel WithImage(string source)
        => new DocumentBuilder().Page(p => p.Image(source, width: 20, height: 20)).Build();

    [NativeRequiredFact]
    public void Render_ResourceNameNotAmongTheResources_Throws()
    {
        var resources = new PdfResources { ["logo"] = Pragmatic.Imaging.QrCode.GeneratePng("x", moduleSize: 2, margin: 1) };

        var act = () => PdfRenderer.Render(WithImage("resource:missing"), resources);

        act.Should().Throw<PdfRenderException>();
    }

    [NativeRequiredFact]
    public void Render_DataUri_Throws()
    {
        var png = Pragmatic.Imaging.QrCode.GeneratePng("x", moduleSize: 2, margin: 1);

        var act = () => PdfRenderer.Render(WithImage("data:image/png;base64," + Convert.ToBase64String(png)));

        act.Should().Throw<PdfRenderException>();
    }

    [NativeRequiredFact]
    public void Render_ResourceNamePresent_ProducesAPdf()
    {
        var resources = new PdfResources { ["logo"] = Pragmatic.Imaging.QrCode.GeneratePng("x", moduleSize: 2, margin: 1) };

        var pdf = PdfRenderer.Render(WithImage("resource:logo"), resources);

        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }
}

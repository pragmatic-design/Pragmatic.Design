using Pragmatic.Documents.Model;
using Pragmatic.Testing.Assertions;
using Pragmatic.Testing.Visual;

namespace Pragmatic.Documents.Pdf.Tests;

/// <summary>
///     A page this renderer writes is rasterised by PDFium, an independent renderer, into an image the
///     visual comparisons can hold to a reference; and the rasterisation is fixed, so the same page gives
///     the same pixels every time.
/// </summary>
public sealed class APageRasterisesThroughAnIndependentRendererTests
{
    private const int DotsPerInch = 96;

    private static byte[] Pdf() => PdfRenderer.Render(new DocumentModel
    {
        Title = "Rasterised",
        Pages =
        [
            new DocumentPage
            {
                Content =
                [
                    new HeadingNode { Content = "A page compared as an image", Level = 1 },
                    new TextNode { Content = "PDFium draws what this renderer wrote." },
                ],
            },
        ],
    });

    [NativeRequiredFact]
    public void Render_OfOnePageTwice_GivesTheSamePixels()
    {
        var pdf = Pdf();

        var first = PdfPageRasterizer.Render(pdf, 0, DotsPerInch);
        var second = PdfPageRasterizer.Render(pdf, 0, DotsPerInch);
        for (var y = 100; y < 140; y++)
        {
            for (var x = 90; x < 400; x++)
                second.Pixels[(y * second.Width + x) * 4] = (byte)Math.Max(0, second.Pixels[(y * second.Width + x) * 4] - 60);
        }

        VisualComparison.AssertSimilar(second, first, VisualThresholds.Identical, nameof(Render_OfOnePageTwice_GivesTheSamePixels));
    }

    [NativeRequiredFact]
    public void Render_OfAPage_IsThePageAtTheResolutionAskedWithItsTextDrawn()
    {
        var pdf = Pdf();

        PdfPageRasterizer.PageCount(pdf).Should().Be(1);
        var page = PdfPageRasterizer.Render(pdf, 0, DotsPerInch);
        var atDouble = PdfPageRasterizer.Render(pdf, 0, 2 * DotsPerInch);

        Math.Abs(atDouble.Width - 2 * page.Width).Should().BeLessThanOrEqualTo(1, "twice the resolution is twice the pixels, give or take the rounding");
        Math.Abs(atDouble.Height - 2 * page.Height).Should().BeLessThanOrEqualTo(1);
        DarkPixels(page).Should().BeGreaterThan(100, "the heading and the paragraph are drawn in a dark ink on a white page");
    }

    private static int DarkPixels(RgbaImage image)
    {
        var count = 0;
        for (var i = 0; i < image.Pixels.Length; i += 4)
        {
            if (0.299 * image.Pixels[i] + 0.587 * image.Pixels[i + 1] + 0.114 * image.Pixels[i + 2] < 128)
                count++;
        }

        return count;
    }
}

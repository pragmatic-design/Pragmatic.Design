using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Pdf.Tests;

public class PdfRenderOptionsTests
{
    private static DocumentModel MultiPage(int pageCount)
    {
        var pages = new List<DocumentPage>(pageCount);
        for (var i = 0; i < pageCount; i++)
            pages.Add(new DocumentPage
            {
                Content = [new TextNode { Content = $"Page {i + 1}" }]
            });
        return new DocumentModel { Title = "Pages", Pages = pages };
    }

    /// <summary>
    ///     A document of many pages keeps its document information. The removed <c>BatchPageCount</c> rendered
    ///     each batch of pages on its own and merged them, and the merged file named no /Info at all (#294).
    /// </summary>
    [Fact]
    public void Render_ManyPages_KeepsTheDocumentInformation()
    {
        var pdf = PdfRenderer.Render(MultiPage(25));

        System.Text.RegularExpressions.Regex.IsMatch(
            System.Text.Encoding.Latin1.GetString(pdf), @"trailer\s*<<[^>]*/Info\s+\d+\s+\d+\s+R").Should().BeTrue();
        PdfOperations.GetPageCount(pdf).Should().Be(25u);
    }

    [Fact]
    public void Render_NullOptions_RendersNormally()
    {
        var model = MultiPage(3);

        var pdf = PdfRenderer.Render(model);

        pdf.Should().StartWith("%PDF"u8.ToArray());
    }

    [Fact]
    public void Render_WithCustomFonts_ThrowsNotSupported()
    {
        var model = MultiPage(1);
        var options = new PdfRenderOptions { CustomFonts = [[0x00, 0x01]] };

        var act = () => PdfRenderer.Render(model, options: options);

        act.Should().Throw<NotSupportedException>().WithMessage("*CustomFonts*");
    }

    [Fact]
    public void Render_WithMaxImageDimension_ThrowsNotSupported()
    {
        var model = MultiPage(1);
        var options = new PdfRenderOptions { MaxImageDimension = 2000 };

        var act = () => PdfRenderer.Render(model, options: options);

        act.Should().Throw<NotSupportedException>().WithMessage("*MaxImageDimension*");
    }
}

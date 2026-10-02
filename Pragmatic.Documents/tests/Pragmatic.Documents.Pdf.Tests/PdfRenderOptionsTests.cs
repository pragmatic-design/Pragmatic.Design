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
        return new DocumentModel { Title = "Batched", Pages = pages };
    }

    [Fact]
    public void Render_WithBatching_ProducesValidMergedPdf()
    {
        var model = MultiPage(5);

        var pdf = PdfRenderer.Render(model, options: new PdfRenderOptions { BatchPageCount = 2 });

        pdf.Should().StartWith("%PDF"u8.ToArray());
        pdf.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Render_BatchLargerThanDocument_StillProducesPdf()
    {
        // Fewer pages than the batch size → batching path is skipped, single compile.
        var model = MultiPage(2);

        var pdf = PdfRenderer.Render(model, options: new PdfRenderOptions { BatchPageCount = 20 });

        pdf.Should().StartWith("%PDF"u8.ToArray());
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

    [Fact]
    public void ServerPreset_RendersSuccessfully()
    {
        // The Server preset batches pages to bound memory and deliberately does NOT set
        // MaxImageDimension, so it renders normally. (Image downscaling remains guarded on its own —
        // see Render_WithMaxImageDimension_ThrowsNotSupported.)
        var model = MultiPage(3);

        var pdf = PdfRenderer.Render(model, options: PdfRenderOptions.Server);

        pdf.Should().StartWith("%PDF"u8.ToArray());
        pdf.Length.Should().BeGreaterThan(0);
    }
}

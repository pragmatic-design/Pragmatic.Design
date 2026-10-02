using Pragmatic.Testing.Assertions;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Tests;

public class PdfRendererAsyncTests
{
    [NativeRequiredFact]
    public async Task RenderAsync_SimpleDocument_ProducesPdf()
    {
        var model = new DocumentModel
        {
            Title = "Async Test",
            Pages = [new DocumentPage
            {
                Content =
                [
                    new HeadingNode { Content = "Async Rendering", Level = 1 },
                    new TextNode { Content = "This was rendered asynchronously." }
                ]
            }]
        };

        var pdf = await PdfRenderer.RenderAsync(model);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public async Task RenderToStreamAsync_WritesToStream()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [new TextNode { Content = "Stream async" }] }]
        };

        using var ms = new MemoryStream();
        await PdfRenderer.RenderToStreamAsync(ms, model);

        ms.Length.Should().BeGreaterThan(0);
        ms.Position = 0;
        var header = new byte[4];
        await ms.ReadExactlyAsync(header);
        System.Text.Encoding.ASCII.GetString(header).Should().Be("%PDF");
    }

    [NativeRequiredFact]
    public async Task RenderAsync_Cancellation_ThrowsOperationCanceled()
    {
        var model = new DocumentModel
        {
            Pages = [new DocumentPage { Content = [new TextNode { Content = "Cancel" }] }]
        };

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => PdfRenderer.RenderAsync(model, ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [NativeRequiredFact]
    public async Task RenderAsync_WithResources_ProducesPdf()
    {
        var model = new DocumentBuilder()
            .Title("Async with Resources")
            .Page(p => p
                .Heading("Document with Image")
                .Image("resource:logo", width: 100, height: 50)
            )
            .Build();

        // Create a small PNG as a resource
        var qr = Pragmatic.Imaging.QrCode.GeneratePng("test", moduleSize: 2, margin: 1);
        var resources = new PdfResources { ["logo"] = qr };

        var pdf = await PdfRenderer.RenderAsync(model, resources);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf[..4]).Should().Be("%PDF");
    }
}

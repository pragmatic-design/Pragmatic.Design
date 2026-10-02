using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
///     Minimum viable PDF: metadata + one page with a heading and a paragraph.
///     The fluent builder mirrors the final document structure; PdfRenderer turns it
///     into bytes in one call.
/// </summary>
public static class BasicPdfSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Basic PDF (metadata + heading + paragraph) ---");

        var model = new DocumentBuilder()
            .Title("Pragmatic.Design Preview")
            .Author("Alessandro Saiani")
            .Subject("PDF rendering sample")
            .Keywords("pragmatic;pdf;sample")
            .Language("en")
            .Page(p => p
                .Heading("Pragmatic.Design — PDF rendering", level: 1)
                .Heading("Hello, world", level: 2)
                .Text(
                    "Pragmatic.Documents.Pdf renders a DocumentModel into a PDF byte array. " +
                    "No runtime dependencies, no headless browser, no templating language. " +
                    "The same model can be sent to the DOCX or spreadsheet renderers."))
            .Build();

        var bytes = PdfRenderer.Render(model);
        var path = Path.Combine(outputDir, "basic.pdf");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  basic.pdf              {bytes.Length} bytes");
        Console.WriteLine();
    }
}

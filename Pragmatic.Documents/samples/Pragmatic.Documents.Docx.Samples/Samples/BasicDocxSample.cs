using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     Smallest DOCX: metadata + one page with heading and paragraph.
///     The DocumentModel is the same one we'd feed to PdfRenderer; only the
///     renderer changes.
/// </summary>
public static class BasicDocxSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Basic DOCX (metadata + heading + paragraph) ---");

        var model = new DocumentBuilder()
            .Title("Pragmatic.Design Preview")
            .Author("Alessandro Saiani")
            .Subject("DOCX rendering sample")
            .Keywords("pragmatic;docx;sample")
            .Language("en")
            .Page(p => p
                .Heading("Pragmatic.Design — DOCX rendering", level: 1)
                .Heading("Hello, world", level: 2)
                .Text(
                    "Pragmatic.Documents.Docx renders a DocumentModel into Office Open XML bytes. " +
                    "The output opens natively in Word, LibreOffice Writer, Pages, and Google Docs. " +
                    "Hot-swap the renderer for PdfRenderer to get the same content as PDF."))
            .Build();

        var bytes = DocxRenderer.Render(model);
        var path = Path.Combine(outputDir, "basic.docx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  basic.docx             {bytes.Length} bytes");
        Console.WriteLine();
    }
}

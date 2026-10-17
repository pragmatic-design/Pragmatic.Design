using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
/// Page setup and document metadata for PDF output. Paper size, orientation, and
/// margins are document-level settings on <see cref="DocumentBuilder"/>; title,
/// author, subject, keywords and custom key/value pairs flow into the PDF
/// document information dictionary.
///
/// Note: <c>PdfRenderOptions</c> holds custom fonts and image down-scaling, neither
/// of which the native engine applies yet: setting either makes
/// <see cref="PdfRenderer"/> throw <see cref="System.NotSupportedException"/>, so
/// neither is demonstrated here. This sample shows the page setup that IS
/// available through the rendering pipeline today.
/// </summary>
public static class PdfPageSetupSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Page setup (size + orientation + margins + metadata) ---");

        var model = new DocumentBuilder()
            .Title("Quarterly Report")
            .Author("Pragmatic.Documents")
            .Subject("Page setup demonstration")
            .Keywords("pdf;orientation;metadata")
            .Language("en")
            .Meta("Department", "Finance")          // custom info-dictionary entry
            .Size(PageSize.A4)
            .Landscape()                            // document-level orientation
            .WithMargins(Margins.Narrow)
            .Page(p => p
                .Heading("Landscape A4, narrow margins", level: 1)
                .Text("Paper size, orientation and margins are set once on the document builder.")
                .Text("Document metadata (title/author/subject/keywords + custom Meta) is embedded in the PDF."))
            .Build();

        var bytes = PdfRenderer.Render(model);

        var path = Path.Combine(outputDir, "page-setup.pdf");
        File.WriteAllBytes(path, bytes);

        Console.WriteLine($"  page-setup.pdf         {bytes.Length} bytes ({PdfOperations.GetPageCount(bytes)} page, A4 landscape, narrow margins)");
        Console.WriteLine();
    }
}

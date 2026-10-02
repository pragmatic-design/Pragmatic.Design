using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
///     Multi-page document with explicit PageBreak and a per-page mix of
///     headings, paragraphs, and a horizontal rule. Each call to Page() on
///     the builder starts a new page; PageBreak() inside a page forces a
///     break without starting a new logical page.
/// </summary>
public static class MultiPageSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Multi-page document ---");

        var model = new DocumentBuilder()
            .Title("Pragmatic.Design — Preview 0 release notes")
            .Author("Alessandro Saiani")
            .Page(p => p
                .Heading("Preview 0 — Release Notes", level: 1)
                .Text(
                    "Preview 0 is the first tagged release of Pragmatic.Design. " +
                    "The goal of this preview is to validate the release infrastructure " +
                    "(versioning, packaging, CI) and to shake out issues via internal " +
                    "adoption before publishing publicly.")
                .Spacer()
                .Heading("What's included", level: 2)
                .Text(
                    "38 runtime modules across Foundation, Capabilities, and Integration tiers. " +
                    "Every module packs with a LICENSE, a README, and SourceLink metadata. " +
                    "Tier 3 experimental modules ship with [Experimental] markers."))
            .Page(p => p
                .Heading("Known limitations", level: 2)
                .Text("Preview 0 is explicitly not production-ready. Notable caveats:")
                .Spacer(5)
                .Text("  - Pragmatic.Imaging ships only win-x64 native binaries.")
                .Text("  - Pragmatic.Messaging multi-bus wiring is marked [Experimental].")
                .Text("  - External package consumers should validate against a local BaGetter feed.")
                .Spacer()
                .HorizontalRule()
                .Heading("Feedback", level: 2)
                .Text(
                    "Report issues or request features through the GitHub repository. " +
                    "Commercial inquiries: https://www.pragmaticdesign.net"))
            .Build();

        var bytes = PdfRenderer.Render(model);
        var path = Path.Combine(outputDir, "release-notes.pdf");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  release-notes.pdf      {bytes.Length} bytes (2 pages)");
        Console.WriteLine();
    }
}

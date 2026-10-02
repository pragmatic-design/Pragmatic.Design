using Pragmatic.Documents.Docx;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Samples.Samples;

/// <summary>
///     Multi-page DOCX with a table of contents generated from headings. Toc()
///     produces a field that Word resolves when the document is opened. Each
///     heading becomes an entry; MaxLevel caps the depth.
/// </summary>
public static class MultiPageWithTocSample
{
    public static void Run(string outputDir)
    {
        Console.WriteLine("--- Multi-page DOCX with ToC ---");

        var model = new DocumentBuilder()
            .Title("Pragmatic.Design — Preview 0 release notes")
            .Author("Alessandro Saiani")
            .Page(p => p
                .Heading("Preview 0 — Release Notes", level: 1)
                .Toc(maxLevel: 2, title: "Table of contents")
                .Spacer())
            .Page(p => p
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

        var bytes = DocxRenderer.Render(model);
        var path = Path.Combine(outputDir, "release-notes.docx");
        File.WriteAllBytes(path, bytes);
        Console.WriteLine($"  release-notes.docx     {bytes.Length} bytes (3 pages, ToC auto-populates on open)");
        Console.WriteLine();
    }
}

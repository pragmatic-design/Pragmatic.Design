using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
/// Post-processing on already-rendered PDF bytes with <see cref="PdfOperations"/>:
/// page count, merge (sync + async) and page-range split — all backed by the
/// native engine.
/// </summary>
public static class PdfOperationsSample
{
    public static async Task Run(string outputDir)
    {
        Console.WriteLine("--- Merge / split / page-count (PdfOperations) ---");

        // Two source PDFs: A has two pages, B has one.
        var docA = new DocumentBuilder()
            .Page(p => p.Heading("Document A — Page 1", level: 1).Text("First page of A."))
            .Page(p => p.Heading("Document A — Page 2", level: 1).Text("Second page of A."))
            .Build();
        var docB = new DocumentBuilder()
            .Page(p => p.Heading("Document B — Page 1", level: 1).Text("First page of B."))
            .Build();

        var pdfA = PdfRenderer.Render(docA);
        var pdfB = PdfRenderer.Render(docB);

        Console.WriteLine($"  GetPageCount           A = {PdfOperations.GetPageCount(pdfA)} pages, B = {PdfOperations.GetPageCount(pdfB)} page(s)");

        // Merge (sync) -> single PDF combining all pages.
        var merged = PdfOperations.Merge(pdfA, pdfB);
        Console.WriteLine($"  Merge                  {merged.Length} bytes, {PdfOperations.GetPageCount(merged)} pages total");

        // Merge (async) -> same result off the calling thread.
        var mergedAsync = await PdfOperations.MergeAsync([pdfA, pdfB]);
        Console.WriteLine($"  MergeAsync             {PdfOperations.GetPageCount(mergedAsync)} pages");

        // Split -> extract a 1-based, inclusive page range as a new PDF.
        var firstTwo = PdfOperations.Split(merged, fromPage: 1, toPage: 2);
        Console.WriteLine($"  Split(1..2)            {PdfOperations.GetPageCount(firstTwo)} pages");

        var lastPage = await PdfOperations.SplitAsync(merged, fromPage: 3, toPage: 3);
        Console.WriteLine($"  SplitAsync(3..3)       {PdfOperations.GetPageCount(lastPage)} page");

        var mergedPath = Path.Combine(outputDir, "operations-merged.pdf");
        File.WriteAllBytes(mergedPath, merged);
        Console.WriteLine($"  -> wrote {Path.GetFileName(mergedPath)}");
        Console.WriteLine();
    }
}

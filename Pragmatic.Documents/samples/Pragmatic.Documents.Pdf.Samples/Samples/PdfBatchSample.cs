using Pragmatic.Documents.Model;
using Pragmatic.Documents.Pdf;

namespace Pragmatic.Documents.Pdf.Samples.Samples;

/// <summary>
/// Batch rendering with <see cref="PdfBatch"/>: render many documents in parallel
/// (bounded concurrency), and optionally merge them into a single PDF.
/// </summary>
public static class PdfBatchSample
{
    public static async Task Run(string outputDir)
    {
        Console.WriteLine("--- Batch render + render-and-merge (PdfBatch) ---");

        // Build a handful of independent one-page documents (e.g. per-customer invoices).
        var documents = Enumerable.Range(1, 4)
            .Select(i => new DocumentBuilder()
                .Title($"Invoice #{1000 + i}")
                .Page(p => p
                    .Heading($"Invoice #{1000 + i}", level: 1)
                    .Text($"This is invoice number {1000 + i} rendered as part of a batch."))
                .Build())
            .ToList();

        // 1) Parallel batch: one byte[] per document, order preserved.
        var rendered = await PdfBatch.RenderAsync(documents);
        Console.WriteLine($"  RenderAsync            {rendered.Length} PDFs, sizes = [{string.Join(", ", rendered.Select(r => r.Length))}] bytes");

        // 2) Parallel render + merge into a single multi-document PDF.
        var merged = await PdfBatch.RenderAndMergeAsync(documents);
        var mergedPath = Path.Combine(outputDir, "batch-merged.pdf");
        File.WriteAllBytes(mergedPath, merged);
        Console.WriteLine($"  RenderAndMergeAsync    {merged.Length} bytes ({PdfOperations.GetPageCount(merged)} pages) -> {Path.GetFileName(mergedPath)}");

        // 3) Bound the degree of parallelism explicitly (e.g. constrained host).
        var renderedBounded = await PdfBatch.RenderAsync(documents, maxConcurrency: 2);
        Console.WriteLine($"  RenderAsync(maxConc=2) {renderedBounded.Length} PDFs");
        Console.WriteLine();
    }
}

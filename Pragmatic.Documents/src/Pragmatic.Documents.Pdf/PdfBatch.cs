using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Pdf;

/// <summary>
/// Batch PDF rendering with bounded concurrency.
/// </summary>
public static class PdfBatch
{
    private static readonly int DefaultConcurrency = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Render multiple DocumentModels to separate PDFs in parallel.
    /// </summary>
    public static async Task<byte[][]> RenderAsync(
        IReadOnlyList<DocumentModel> models,
        int maxConcurrency = 0,
        CancellationToken ct = default)
    {
        if (models.Count == 0) return [];

        var concurrency = maxConcurrency > 0 ? maxConcurrency : DefaultConcurrency;
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        var tasks = new Task<byte[]>[models.Count];
        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            tasks[i] = RenderOneAsync(model, semaphore, ct);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// Render multiple DocumentModels and merge them into a single PDF.
    /// </summary>
    public static async Task<byte[]> RenderAndMergeAsync(
        IReadOnlyList<DocumentModel> models,
        int maxConcurrency = 0,
        CancellationToken ct = default)
    {
        var pdfs = await RenderAsync(models, maxConcurrency, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return await PdfOperations.MergeAsync(pdfs, ct).ConfigureAwait(false);
    }

    private static async Task<byte[]> RenderOneAsync(
        DocumentModel model,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => PdfRenderer.Render(model), ct).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}

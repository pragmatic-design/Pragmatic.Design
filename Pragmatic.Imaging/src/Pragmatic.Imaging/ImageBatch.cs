namespace Pragmatic.Imaging;

/// <summary>
/// Batch image processing with bounded concurrency.
/// </summary>
public static class ImageBatch
{
    private static readonly int DefaultConcurrency = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>
    /// Generate thumbnails for multiple images in parallel.
    /// </summary>
    public static Task<byte[][]> ThumbnailsAsync(
        IReadOnlyList<byte[]> images,
        uint maxWidth, uint maxHeight,
        ImageFormat format = ImageFormat.Png, byte quality = 90,
        int maxConcurrency = 0,
        ImagingOptions? options = null,
        CancellationToken ct = default)
    {
        return ProcessAsync(images, data =>
        {
            using var pipeline = ImagePipeline.Load(data.Span, options);
            pipeline.Thumbnail(maxWidth, maxHeight);
            return pipeline.Encode(format, quality);
        }, maxConcurrency, ct);
    }

    /// <summary>
    /// Convert multiple images to a target format in parallel.
    /// </summary>
    public static Task<byte[][]> ConvertAsync(
        IReadOnlyList<byte[]> images,
        ImageFormat format, byte quality = 90,
        int maxConcurrency = 0,
        ImagingOptions? options = null,
        CancellationToken ct = default)
    {
        return ProcessAsync(images, data =>
        {
            using var pipeline = ImagePipeline.Load(data.Span, options);
            return pipeline.Encode(format, quality);
        }, maxConcurrency, ct);
    }

    /// <summary>
    /// Process multiple images in parallel with a custom operation.
    /// The operation receives <see cref="ReadOnlyMemory{T}"/> and returns the processed byte[].
    /// On partial failure, throws <see cref="AggregateException"/> whose inner exceptions are
    /// <see cref="ImageBatchItemException"/> instances that carry the original input index.
    /// </summary>
    public static async Task<byte[][]> ProcessAsync(
        IReadOnlyList<byte[]> images,
        Func<ReadOnlyMemory<byte>, byte[]> operation,
        int maxConcurrency = 0,
        CancellationToken ct = default)
    {
        if (images.Count == 0) return [];

        var concurrency = maxConcurrency > 0 ? maxConcurrency : DefaultConcurrency;
        using var semaphore = new SemaphoreSlim(concurrency, concurrency);

        var tasks = new Task<byte[]>[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            var index = i;
            var data = images[i];
            if (data is null)
                throw new ArgumentException($"images[{index}] is null.", nameof(images));
            tasks[i] = ProcessOneAsync(index, data, operation, semaphore, ct);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private static async Task<byte[]> ProcessOneAsync(
        int index,
        byte[] data,
        Func<ReadOnlyMemory<byte>, byte[]> operation,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => operation(data), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wrap with index so callers can correlate failures to input positions.
            throw new ImageBatchItemException(index, ex);
        }
        finally
        {
            semaphore.Release();
        }
    }
}


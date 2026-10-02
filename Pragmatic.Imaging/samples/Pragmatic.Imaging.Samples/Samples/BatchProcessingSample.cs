using Pragmatic.Imaging;

namespace Pragmatic.Imaging.Samples.Samples;

/// <summary>
///     Bounded-concurrency batch processing via <see cref="ImageBatch"/>.
///     Shows the high-level <c>ThumbnailsAsync</c> / <c>ConvertAsync</c> helpers plus the
///     general-purpose <c>ProcessAsync</c> with a custom operation. On partial failure,
///     <c>ProcessAsync</c> surfaces an <see cref="ImageBatchItemException"/> carrying the
///     input index, so callers can correlate a failure back to the offending image.
/// </summary>
public static class BatchProcessingSample
{
    public static async Task RunAsync(string outputDir)
    {
        Console.WriteLine("--- Batch processing (ProcessAsync custom op + helpers) ---");

        // Build a small batch of distinct source images.
        var batch = new List<byte[]>
        {
            QrCode.GeneratePng("batch-item-0", moduleSize: 6),
            QrCode.GeneratePng("batch-item-1", moduleSize: 8),
            QrCode.GeneratePng("batch-item-2", moduleSize: 10),
        };
        Console.WriteLine($"  batch size            {batch.Count} images");

        // 1) Custom operation: grayscale + resize each image to 128x128 WebP.
        //    The operation receives ReadOnlyMemory<byte> and returns the processed byte[].
        var processed = await ImageBatch.ProcessAsync(batch, data =>
        {
            using var pipeline = ImagePipeline.Load(data.Span);
            pipeline.Grayscale();
            pipeline.Resize(128, 128);
            return pipeline.Encode(ImageFormat.WebP, quality: 75);
        });

        for (var i = 0; i < processed.Length; i++)
        {
            File.WriteAllBytes(Path.Combine(outputDir, $"batch-custom-{i}.webp"), processed[i]);
            Console.WriteLine($"  batch-custom-{i}.webp   {processed[i].Length} bytes (grayscale + 128x128 WebP)");
        }

        // 2) Helper: ThumbnailsAsync — same fit-within-bounds thumbnail for the whole batch.
        var thumbs = await ImageBatch.ThumbnailsAsync(batch, maxWidth: 64, maxHeight: 64, ImageFormat.Png);
        for (var i = 0; i < thumbs.Length; i++)
            File.WriteAllBytes(Path.Combine(outputDir, $"batch-thumb-{i}.png"), thumbs[i]);
        Console.WriteLine($"  batch-thumb-*.png     {thumbs.Length} thumbnails (fit 64x64)");

        // 3) Helper: ConvertAsync — convert the whole batch to JPEG in parallel.
        var jpegs = await ImageBatch.ConvertAsync(batch, ImageFormat.Jpeg, quality: 85);
        var totalJpeg = jpegs.Sum(b => b.Length);
        Console.WriteLine($"  batch JPEG convert    {jpegs.Length} images, {totalJpeg} bytes total");

        // 4) Error correlation: ProcessAsync wraps a per-item failure in ImageBatchItemException
        //    whose Index identifies which input failed. Here index 1 is intentionally invalid bytes.
        var withBadItem = new List<byte[]>
        {
            QrCode.GeneratePng("ok-0", moduleSize: 6),
            new byte[] { 0x00, 0x01, 0x02, 0x03 }, // not a decodable image
            QrCode.GeneratePng("ok-2", moduleSize: 6),
        };

        try
        {
            await ImageBatch.ProcessAsync(withBadItem, data =>
            {
                using var pipeline = ImagePipeline.Load(data.Span);
                return pipeline.Encode(ImageFormat.Png);
            });
        }
        catch (ImageBatchItemException ex)
        {
            Console.WriteLine($"  error correlation     failed at input index {ex.Index}: {ex.InnerException?.GetType().Name}");
        }
        catch (AggregateException agg) when (agg.InnerException is ImageBatchItemException itemEx)
        {
            Console.WriteLine($"  error correlation     failed at input index {itemEx.Index}: {itemEx.InnerException?.GetType().Name}");
        }

        Console.WriteLine();
    }
}

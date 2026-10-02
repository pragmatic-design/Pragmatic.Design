namespace Pragmatic.Imaging;

/// <summary>
/// Exception thrown when a single item in an <see cref="ImageBatch"/> operation fails.
/// Carries the original <see cref="Index"/> so callers can correlate failure to the input list.
/// </summary>
public sealed class ImageBatchItemException(int index, Exception innerException)
    : Exception($"Image at index {index} failed: {innerException.Message}", innerException)
{
    /// <summary>Zero-based index of the failed image in the input list.</summary>
    public int Index { get; } = index;
}

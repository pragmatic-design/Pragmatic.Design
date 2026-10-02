namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Context passed to batch item handlers, linking back to the batch.
/// </summary>
/// <param name="BatchId">The batch this item belongs to.</param>
/// <param name="ItemIndex">Zero-based index of this item in the batch.</param>
/// <param name="TotalItems">Total items in the batch.</param>
public sealed record BatchContext(Guid BatchId, int ItemIndex, int TotalItems);

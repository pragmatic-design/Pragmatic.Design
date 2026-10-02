namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Splits a batch into individual items for parallel processing.
///     Each item is published as a separate message.
/// </summary>
/// <typeparam name="TBatch">The batch command/request type.</typeparam>
/// <typeparam name="TItem">The individual item type to process.</typeparam>
public interface IBatchSplitter<in TBatch, TItem>
{
    /// <summary>Splits the batch into individual items.</summary>
    IReadOnlyList<TItem> Split(TBatch batch);
}

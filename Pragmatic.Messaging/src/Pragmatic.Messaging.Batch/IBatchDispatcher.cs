using Pragmatic.Composition.Attributes;

namespace Pragmatic.Messaging.Batch;

/// <summary>
///     Splits a batch, publishes its items with the batch headers, and tracks its progress — what an
///     operation depends on to start a batch (<see cref="BatchDispatcher{TBatch,TItem}" />).
/// </summary>
/// <remarks>
///     An interface so a generated operation can take it as a field: the generator injects a dependency it
///     can tell from plain state, and a concrete class it cannot (PRAG0419). Registered for every pair by
///     <c>EnableBatchProcessing</c>; the host registers the <see cref="IBatchSplitter{TBatch,TItem}" />.
/// </remarks>
/// <typeparam name="TBatch">The batch command type.</typeparam>
/// <typeparam name="TItem">The individual item type published to the bus.</typeparam>
[ProvidedByHost(Lifetime.Scoped)]
public interface IBatchDispatcher<in TBatch, TItem>
{
    /// <inheritdoc cref="BatchDispatcher{TBatch,TItem}.DispatchAsync" />
    Task<Guid> DispatchAsync(TBatch batch, string? label = null, CancellationToken ct = default);

    /// <inheritdoc cref="BatchDispatcher{TBatch,TItem}.ResumeAsync" />
    Task<Guid> ResumeAsync(TBatch batch, Guid batchId, CancellationToken ct = default);
}

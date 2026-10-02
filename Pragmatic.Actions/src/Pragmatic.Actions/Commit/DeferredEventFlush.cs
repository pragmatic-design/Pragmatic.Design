using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Actions.Commit;

/// <summary>
///     Dispatches the events that nested invocations deferred, once the owner has committed.
/// </summary>
/// <remarks>
///     <para>
///         A mutation that does not commit does not dispatch either — its events go into the batch the
///         owner opened. Without this they stay there: the operation succeeds, the rows are right, and
///         the handler never runs. Measured before it existed, on an invoice that stayed <c>Draft</c>
///         because the cancellation that should have voided it was never announced.
///     </para>
///     <para>
///         After the commit, and isolated from it: a handler that throws must not turn a committed
///         operation into a reported failure, or the caller retries work that already happened.
///     </para>
/// </remarks>
public static class DeferredEventFlush
{
    /// <summary>Dispatches everything accumulated in <paramref name="batch" />, if anything was.</summary>
    /// <param name="batch">The owner's batch, or <c>null</c> when this invocation owned nothing.</param>
    /// <param name="serviceProvider">Resolves the dispatcher; absent means Events is not wired.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task FlushAsync(
        BatchContext? batch, IServiceProvider serviceProvider, CancellationToken ct)
    {
        if (batch is null || batch.DeferredEvents.Count == 0)
            return;

        var dispatcher = serviceProvider.GetService<IDomainEventDispatcher>();
        if (dispatcher is null)
            return;

        await dispatcher.DispatchAsync(batch.DeferredEvents.Cast<IDomainEvent>(), ct).ConfigureAwait(false);
    }
}

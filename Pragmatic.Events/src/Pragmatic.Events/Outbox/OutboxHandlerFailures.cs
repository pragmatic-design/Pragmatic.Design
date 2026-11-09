using System.Runtime.ExceptionServices;

namespace Pragmatic.Events.Outbox;

/// <summary>
///     The handler failures of one outbox delivery, collected while it dispatches.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="InMemoryEventDispatcher" /> isolates handlers: one that throws is logged and the next still
///         runs. In-process that is the documented contract. During an outbox delivery it made a failed handler
///         look like a delivered event: the dispatch returned normally, the store marked the entry processed,
///         and the handler never ran again. Inside a delivery the dispatcher still runs every handler, and it
///         also records each failure here, so the delivery can fail as a whole and the store counts the attempt.
///     </para>
///     <para>
///         Ambient (an <see cref="AsyncLocal{T}" />) rather than a parameter, because the dispatcher is reached
///         through <see cref="IDomainEventDispatcher" />, a public contract, and only the in-memory implementation
///         isolates failures. Outside a delivery nothing is collected, and nothing changes.
///     </para>
/// </remarks>
internal sealed class OutboxHandlerFailures
{
    private static readonly AsyncLocal<OutboxHandlerFailures?> Current = new();

    private readonly List<Exception> _failures = [];

    /// <summary>Starts collecting for the delivery that runs inside the returned scope.</summary>
    public static Scope Begin()
    {
        var previous = Current.Value;
        var collector = new OutboxHandlerFailures();
        Current.Value = collector;
        return new Scope(collector, previous);
    }

    /// <summary>Records <paramref name="failure" /> against the delivery in progress; nothing outside one.</summary>
    public static void Record(Exception failure) => Current.Value?._failures.Add(failure);

    /// <summary>Restores the previous collector when disposed, and fails the delivery when a handler failed.</summary>
    public readonly struct Scope(OutboxHandlerFailures collector, OutboxHandlerFailures? previous) : IDisposable
    {
        /// <summary>
        ///     Throws what the handlers threw: the failure itself when there was one, so the stack the store
        ///     records is the handler's, and an <see cref="AggregateException" /> carrying each when there were
        ///     several.
        /// </summary>
        public void ThrowIfAnyHandlerFailed()
        {
            switch (collector._failures.Count)
            {
                case 0:
                    return;
                case 1:
                    ExceptionDispatchInfo.Capture(collector._failures[0]).Throw();
                    return;
                default:
                    throw new AggregateException(
                        $"{collector._failures.Count} event handlers failed during an outbox delivery.",
                        collector._failures);
            }
        }

        /// <inheritdoc />
        public void Dispose() => Current.Value = previous;
    }
}

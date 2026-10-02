namespace Pragmatic.Messaging.Batch;

/// <summary>
///     The outcome of the batch item the current message scope is handling, as its handler decides it.
///     Scoped: one per message.
/// </summary>
/// <remarks>
///     <para>
///         A handler that throws fails its item, but it also has the item retried and dead-lettered and
///         its transaction rolled back. An item that was <b>processed</b>, with part of its work refused —
///         an import part whose valid rows were applied and whose invalid ones were not — calls
///         <see cref="Fail" /> instead, returns normally, and <see cref="BatchProgressMiddleware" /> counts
///         it as failed.
///     </para>
///     <para>
///         Why the batch failed is the application's to record: the progress store keeps counts, not reasons.
///     </para>
/// </remarks>
public sealed class BatchItemOutcome
{
    /// <summary>Whether the handler marked the item failed.</summary>
    public bool Failed { get; private set; }

    /// <summary>Marks the item failed. Counted once, by the middleware, when the handler returns.</summary>
    public void Fail() => Failed = true;
}

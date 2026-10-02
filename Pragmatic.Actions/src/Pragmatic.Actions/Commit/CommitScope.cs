using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Actions.Commit;

/// <summary>
///     Decides, per unit of work, which invoker in a chain performs the commit.
/// </summary>
/// <remarks>
///     <para>
///         Ambient and keyed by the unit of work instance, which is the whole point: an invoker nested
///         inside another that holds the <b>same</b> <see cref="IUnitOfWork" /> stages its writes and
///         lets the outer one save; an invoker holding a <b>different</b> one — another boundary — owns
///         its own commit, because nobody else can save its <c>DbContext</c>.
///     </para>
///     <para>
///         Runtime rather than analysed at compile time, and deliberately: the chain may run through an
///         injected service, a helper, a delegate. Ownership by identity is correct whatever the shape
///         of the call graph, where reading the call sites would only be correct for the shapes the
///         analyser happens to recognise.
///     </para>
/// </remarks>
public static class CommitScope
{
    private static readonly AsyncLocal<Entry?> CurrentEntry = new();

    /// <summary>
    ///     Claims the commit for <paramref name="unitOfWork" />, or reports that an outer invoker
    ///     already holds it.
    /// </summary>
    /// <param name="unitOfWork">The invoker's unit of work, or <c>null</c> when it has no boundary.</param>
    /// <param name="mode">The action's declared strategy.</param>
    /// <returns>
    ///     A handle whose <see cref="Ownership.CommitsHere" /> says whether this invoker saves. Dispose
    ///     it when the invocation ends.
    /// </returns>
    public static Ownership Claim(IUnitOfWork? unitOfWork, CommitMode mode)
        => Claim(unitOfWork, mode, transactional: false);

    /// <summary>
    ///     Claims the commit, optionally declaring that the caller has opened a transaction.
    /// </summary>
    /// <param name="unitOfWork">The invoker's unit of work, or <c>null</c> when it has no boundary.</param>
    /// <param name="mode">The action's declared strategy.</param>
    /// <param name="transactional">
    ///     <c>true</c> when an explicit transaction is open here. The nested steps then <b>do</b> save —
    ///     that is what the transaction is for — while their events still wait for the commit.
    /// </param>
    public static Ownership Claim(IUnitOfWork? unitOfWork, CommitMode mode, bool transactional)
    {
        // No boundary means no unit of work to share, and PerStep opts out of sharing entirely: both
        // commit where they always did.
        if (unitOfWork is null || (mode == CommitMode.PerStep && !transactional))
            return new Ownership(commitsHere: true, previous: null, pushed: false);

        for (var entry = CurrentEntry.Value; entry is not null; entry = entry.Previous)
        {
            if (ReferenceEquals(entry.UnitOfWork, unitOfWork))
                return new Ownership(commitsHere: false, previous: null, pushed: false);
        }

        // A batch opened by hand over this unit of work is a caller saying they will save it. Claiming
        // the commit anyway would save under them, which is the opposite of what they asked for.
        if (CoveringBatch(unitOfWork) is not null)
            return new Ownership(commitsHere: false, previous: null, pushed: false);

        // The owner opens the batch, which is what gives the nested invocations somewhere to put the
        // writes and the events they are not allowed to flush yet. It also makes the invariant the
        // pipelines rely on true by construction: not committing here means an outer claim exists,
        // and an outer claim means a batch is open.
        var batch = new BatchContext(unitOfWork, defersSave: !transactional);
        var previous = CurrentEntry.Value;
        CurrentEntry.Value = new Entry(unitOfWork, previous, batch);
        return new Ownership(commitsHere: true, previous, pushed: true, batch);
    }

    /// <summary>
    ///     The batch opened for <paramref name="unitOfWork" /> by whichever invoker owns its commit, or
    ///     <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    ///     Looked up by unit of work rather than read from <c>BatchContext.Current</c>: the ambient one
    ///     is global, so a root that opened a batch for its own boundary would suppress the commit of a
    ///     mutation belonging to a different one — whose <c>DbContext</c> nobody would then save. That
    ///     is not a hypothesis; it turned 198 Showcase tests red the first time this shipped without it.
    /// </remarks>
    public static BatchContext? BatchFor(IUnitOfWork? unitOfWork)
    {
        if (unitOfWork is null)
            return null;

        for (var entry = CurrentEntry.Value; entry is not null; entry = entry.Previous)
        {
            if (ReferenceEquals(entry.UnitOfWork, unitOfWork))
                return entry.Batch;
        }

        return null;
    }

    /// <summary>
    ///     A hand-opened batch that covers <paramref name="unitOfWork" />, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     One naming a different unit of work is not ours: honouring it would defer a commit its
    ///     opener cannot perform. One naming none covers everything, which is the caller taking
    ///     responsibility for all of it.
    /// </remarks>
    public static BatchContext? CoveringBatch(IUnitOfWork? unitOfWork)
    {
        var ambient = BatchContext.Current;
        if (ambient is null)
            return null;

        return ambient.UnitOfWork is null || ReferenceEquals(ambient.UnitOfWork, unitOfWork)
            ? ambient
            : null;
    }

    /// <summary>
    ///     Steps outside every open claim for the life of the returned handle.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For what runs <b>after</b> the commit: dispatching domain events, flushing what a batch
    ///         deferred, invalidating a cache. Those run inside the <c>using</c> that holds the claim,
    ///         and without a suspension a handler that writes through the same unit of work is read as one
    ///         more nested step — <c>CommitsHere</c> false, the covering batch still deferring — so it
    ///         stages its writes, never saves them, and returns success.
    ///     </para>
    ///     <para>
    ///         Nothing subtle is being worked around: by the time these run the operation <em>has</em>
    ///         committed, so a handler's writes genuinely are their own unit of work and ought to claim
    ///         their own commit. Suspending says that, where leaving the claim in place would say the
    ///         opposite.
    ///     </para>
    ///     <para>
    ///         Suspends the ambient batch too. <see cref="CoveringBatch" /> reads
    ///         <c>BatchContext.Current</c> rather than the claim stack, so clearing only the claims
    ///         would still leave a nested write deferring into a batch nobody will flush again.
    ///     </para>
    /// </remarks>
    public static Suspension Suspend()
    {
        var previous = CurrentEntry.Value;
        CurrentEntry.Value = null;

        return new Suspension(previous, BatchContext.SuspendAmbient());
    }

    /// <summary>Restores the claims and the ambient batch that <see cref="Suspend" /> stepped out of.</summary>
    public readonly struct Suspension(Entry? previous, BatchContext.AmbientSuspension batch) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            batch.Dispose();
            CurrentEntry.Value = previous;
        }
    }

    /// <summary>Whether the current invocation performs the commit for its unit of work.</summary>
    public readonly struct Ownership(bool commitsHere, Entry? previous, bool pushed, BatchContext? batch = null)
        : IDisposable
    {
        /// <summary>True when this invoker is the one that saves.</summary>
        public bool CommitsHere { get; } = commitsHere;

        /// <summary>
        ///     The batch the owner opened, holding the deferred events of everything nested inside it.
        ///     <c>null</c> when this invocation is not the owner, or when the mode shares nothing.
        /// </summary>
        public BatchContext? Batch { get; } = batch;

        /// <inheritdoc />
        public void Dispose()
        {
            Batch?.Dispose();

            if (pushed)
                CurrentEntry.Value = previous;
        }
    }

    /// <summary>One claimed unit of work and the one it nests inside.</summary>
    public sealed class Entry(IUnitOfWork unitOfWork, Entry? previous, BatchContext batch)
    {
        /// <summary>The claimed unit of work, compared by reference.</summary>
        public IUnitOfWork UnitOfWork { get; } = unitOfWork;

        /// <summary>The claim this one nests inside, restored on dispose.</summary>
        public Entry? Previous { get; } = previous;

        /// <summary>Where everything nested under this claim stages its writes and events.</summary>
        public BatchContext Batch { get; } = batch;
    }
}

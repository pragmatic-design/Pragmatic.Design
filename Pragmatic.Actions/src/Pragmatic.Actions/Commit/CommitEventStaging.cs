using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Events;
using Pragmatic.Events.Outbox;
using Pragmatic.Persistence.Lifecycle;

namespace Pragmatic.Actions.Commit;

/// <summary>
///     How the invocation that owns the commit hands its events to an outbox: the ones it declares (<c>[Raises&lt;T&gt;]</c>, #736) and the
///     ones the operations nested in it left in its batch (#800). Staged before its save, so the outbox's capture writes them in the save's
///     transaction; taken after the commit, so it dispatches in process only what no outbox took.
/// </summary>
/// <remarks>
///     <para>
///         The stage exists only where an outbox registered it. Without one, <see cref="Stage" /> answers null: the invoker builds and
///         dispatches its own events after the commit, and <see cref="DeferredEventFlush" /> the batch's, as before an outbox could see them.
///     </para>
///     <para>
///         The batch is read when the capture builds the stage, inside the save, after the generated values are filled: a nested mutation
///         that held its save back deferred the construction of its events, and building them earlier would carry the empty values. What
///         reaches the batch after that read (an entity's events taken once the save is over) was not handed to the outbox, and the flush
///         still dispatches it.
///     </para>
/// </remarks>
public sealed class CommitEventStaging
{
    private readonly DeclaredEventStage _stage;
    private readonly BatchContext? _batch;
    private readonly Func<IReadOnlyList<IDomainEvent>>? _own;
    private readonly HashSet<object> _handed = new(ReferenceEqualityComparer.Instance);

    private CommitEventStaging(DeclaredEventStage stage, BatchContext? batch, Func<IReadOnlyList<IDomainEvent>>? own)
    {
        _stage = stage;
        _batch = batch;
        _own = own;
        stage.Stage(Build);
    }

    /// <summary>
    ///     Stages, for the save about to run, the events <paramref name="own" /> builds and those the operations nested under the owner
    ///     left in <paramref name="batch" />; null when no outbox is registered.
    /// </summary>
    /// <param name="services">Resolves the stage an outbox registers.</param>
    /// <param name="batch">The batch the owner opened, or null when nothing is nested under it.</param>
    /// <param name="own">The events the owner itself declares, or null when it declares none.</param>
    public static CommitEventStaging? Stage(
        IServiceProvider services, BatchContext? batch, Func<IReadOnlyList<IDomainEvent>>? own)
        => services.GetService<DeclaredEventStage>() is { } stage ? new CommitEventStaging(stage, batch, own) : null;

    /// <summary>
    ///     After the commit: what no outbox took, built now; or, with no stage, what <paramref name="own" /> builds, as without an outbox.
    /// </summary>
    /// <remarks>
    ///     With a stage the list holds the nested operations' events too, when no outbox took them; <see cref="DeferredEventFlush" />
    ///     then leaves those out.
    /// </remarks>
    public static IReadOnlyList<IDomainEvent> Remaining(CommitEventStaging? staging, Func<IReadOnlyList<IDomainEvent>> own)
        => staging is null ? own() : staging._stage.Take();

    /// <summary>Empties the stage without building: the save failed, so the operation raised nothing.</summary>
    public void Discard() => _stage.Discard();

    /// <summary>Whether <paramref name="event" />, deferred in the batch, was handed to the save (or to <see cref="Remaining" />).</summary>
    public bool Handed(object @event) => _handed.Contains(@event);

    private IReadOnlyList<IDomainEvent> Build()
    {
        var own = _own?.Invoke() ?? [];
        if (_batch is null)
            return own;

        var events = new List<IDomainEvent>(own);
        foreach (var deferred in _batch.DeferredEvents)
        {
            if (deferred is IDomainEvent domainEvent && _handed.Add(deferred))
                events.Add(domainEvent);
        }

        return events;
    }
}

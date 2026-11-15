namespace Pragmatic.Events.Outbox;

/// <summary>
///     The events an operation declares (<c>[Raises&lt;T&gt;]</c> on a mutation or an action), staged by the invocation that owns the commit
///     so an outbox can take them inside the save, in its transaction, as it takes the events an entity raised.
/// </summary>
/// <remarks>
///     <para>
///         Registered (scoped) by every outbox, and by nothing else: without one the invoker finds no stage and dispatches its declared
///         events in process after the commit, as it always has. With one, a crash between the commit and that dispatch no longer loses
///         them: they are rows in the outbox, delivered by its drainer.
///     </para>
///     <para>
///         <b>What is staged is the construction, not the events.</b> A mutation's event is built from the entity, and a
///         <c>[GeneratedValue]</c> is filled at the top of the save: built earlier, the event would carry the empty value. The capture calls
///         <see cref="Take" /> inside the save, after the values are filled.
///     </para>
///     <para>
///         After the commit the invoker calls <see cref="Take" /> as well and dispatches what it returns: nothing when an outbox took the
///         events, the events themselves when no outbox captured that save (a boundary without one, a save that wrote nothing).
///     </para>
/// </remarks>
public sealed class DeclaredEventStage
{
    private Func<IReadOnlyList<IDomainEvent>>? _build;

    /// <summary>Stages the construction of the declared events of the save about to run, replacing any left from before.</summary>
    public void Stage(Func<IReadOnlyList<IDomainEvent>> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        _build = build;
    }

    /// <summary>The staged events, built now, and the stage emptied: whoever takes them is the one who delivers them.</summary>
    public IReadOnlyList<IDomainEvent> Take()
    {
        var build = _build;
        _build = null;
        return build is null ? [] : build();
    }

    /// <summary>Empties the stage without building: the save failed, so the operation raised nothing.</summary>
    public void Discard() => _build = null;
}

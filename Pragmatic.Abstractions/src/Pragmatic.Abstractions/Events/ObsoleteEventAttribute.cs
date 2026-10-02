namespace Pragmatic.Events;

/// <summary>
///     Marks an event as deprecated and scheduled for removal — the lightweight evolution marker of the
///     two-level event model. Surfaced in the generated AsyncAPI (<c>x-pragmatic-obsolete</c>) so
///     consumers see the deprecation in the contract.
/// </summary>
/// <remarks>
///     Pragmatic events are transient (state-based, dispatched via the outbox — not a persisted event
///     stream), so version upcasting does not apply. Evolve public events additively and detect breaking
///     changes by snapshotting the generated AsyncAPI contract; use this attribute to phase one out.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ObsoleteEventAttribute(string? removeBy = null) : Attribute
{
    /// <summary>Optional free-form deadline/version by which the event will be removed.</summary>
    public string? RemoveBy { get; } = removeBy;
}

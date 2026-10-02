namespace Pragmatic.Events;

/// <summary>
///     Implemented by source-generated entity partials that declare lifecycle events via
///     <c>[Raises&lt;TEvent&gt;(on: ...)]</c>. The persistence interceptor calls this during
///     <c>SavingChanges</c> with the detected lifecycle; the generated body raises the matching
///     events (each event constructor filled from entity members by name) so the existing
///     domain-event dispatch picks them up after the save commits.
/// </summary>
public interface IRaisesLifecycleEvents
{
    /// <summary>Raises the domain events declared for the given lifecycle transition.</summary>
    /// <param name="lifecycle">The transition detected from the entity's change-tracking state.</param>
    void RaiseLifecycleEvents(EntityLifecycle lifecycle);
}

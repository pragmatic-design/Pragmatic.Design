namespace Pragmatic.Events;

/// <summary>
///     The persistence lifecycle transition that raises a declared domain event, used by
///     <c>[Raises&lt;TEvent&gt;(on: ...)]</c> and the lifecycle-events interceptor.
/// </summary>
public enum EntityLifecycle
{
    /// <summary>The entity was inserted (EF <c>Added</c>).</summary>
    Created,

    /// <summary>The entity was updated (EF <c>Modified</c>).</summary>
    Updated,

    /// <summary>The entity was deleted (EF <c>Deleted</c>, or a soft-delete transition).</summary>
    Deleted
}

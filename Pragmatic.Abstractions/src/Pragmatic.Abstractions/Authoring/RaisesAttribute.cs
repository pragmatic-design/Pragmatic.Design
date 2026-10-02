using Pragmatic.Events;

namespace Pragmatic.Authoring;

/// <summary>
///     Declares a domain event the annotated member raises. Repeatable — one per event type.
/// </summary>
/// <remarks>
///     <para>
///         On a <b>mutation or action class</b> the source generator wires the raise, so do not raise
///         the event in the body too or it goes out twice; <see cref="On"/> is ignored there.
///     </para>
///     <para>
///         ⚠️ On an <b>entity's method</b> it generates <b>nothing</b>, and the build now says so:
///         <b>PRAG2753</b> refuses it, naming the declaration and the two shapes that work. The
///         generated lifecycle file carries only the transitions declared on the <b>class</b>, and a
///         generator cannot add statements to a body the author wrote — so without the diagnostic a
///         method's declaration would compile, read as wired, and reach no handler. For a move that is not a lifecycle transition use
///         <c>[RaisesEvent&lt;T&gt;]</c> on the state machine's target member, or call
///         <c>RaiseEvent(...)</c> in the method. The target stays <c>AttributeTargets.Method</c> so the
///         diagnostic can explain instead of the compiler's bare CS0592 — and because on a type that
///         is <i>not</i> an entity the member-level declaration is what the host's event graph reads
///         to attribute a raise to its origin (PRAG0816).
///     </para>
///     <para>
///         On an <b>entity</b>, the source generator wires the raise for you: at the <see cref="On"/>
///         lifecycle transition the lifecycle-events interceptor raises the event, filling its constructor
///         from matching entity members by name — no custom body needed. Stack one per event:
///         <c>[Raises&lt;DrugCreated&gt;]</c>, <c>[Raises&lt;DrugDeleted&gt;(EntityLifecycle.Deleted)]</c>.
///     </para>
/// </remarks>
/// <typeparam name="TEvent">The domain event type raised.</typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RaisesAttribute<TEvent>(EntityLifecycle on = EntityLifecycle.Created) : Attribute
    where TEvent : notnull
{
    /// <summary>The lifecycle transition that raises the event (entity-level only). Defaults to Created.</summary>
    public EntityLifecycle On { get; } = on;
}

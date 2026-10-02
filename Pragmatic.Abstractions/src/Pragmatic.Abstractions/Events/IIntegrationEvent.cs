namespace Pragmatic.Events;

/// <summary>
///     Marker for an <b>integration (public) event</b> — the second level of the two-level event model.
///     Domain events (<see cref="IDomainEvent"/>) are internal to a boundary; integration events are the
///     public, cross-boundary contract: a self-contained, denormalized fact other boundaries may consume.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The second level is a subset of the first, by construction</b>: this interface extends
///         <see cref="IDomainEvent"/>, so an integration event is a domain event that is also
///         published. That is the rule and not an implementation detail — the transactional outbox
///         captures domain events raised by tracked entities, and the generated AsyncAPI catalogues
///         types implementing <see cref="IDomainEvent"/>, so a cross-service contract that is not a
///         domain event gets neither.
///     </para>
///     <para>
///         ⚠️ <c>[PublicEvent]</c> is <b>not</b> equivalent to implementing this interface. The interface makes the type a domain event and marks
///         it published; the attribute only marks it published, and on a type that is not a domain
///         event it is read by nobody (<b>PRAG0836</b>). Implementing this interface is the form that
///         cannot be half-applied.
///     </para>
///     <para>
///         Keep integration events stable and self-contained (carry the data consumers need, not entity
///         references) — they are an API surface. Internal domain events should not be consumed across
///         boundaries; publish a dedicated integration event instead.
///     </para>
/// </remarks>
public interface IIntegrationEvent : IDomainEvent;

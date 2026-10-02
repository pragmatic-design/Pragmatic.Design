namespace Pragmatic.Events.Attributes;

/// <summary>
///     Enables the transactional domain-event outbox for a boundary's generated DbContext.
///     Apply it to the boundary marker class (the <c>[Boundary]</c> type).
/// </summary>
/// <remarks>
///     <para>
///         When present, the source generator wires the outbox for that boundary's DbContext:
///         it maps the <c>__EventOutbox</c> table in <c>OnModelCreating</c>, adds the
///         <c>EventOutboxInterceptor</c> to the context, registers the delivery background
///         service (<c>AddEventOutbox&lt;TContext&gt;()</c>), and includes <c>__EventOutbox</c>
///         in the database schema metadata so migrations create it.
///     </para>
///     <para>
///         Requires the <c>Pragmatic.Events.EFCore</c> package. Domain events raised by entities
///         in this boundary are then captured in the same transaction as the entity change and
///         delivered asynchronously (at-least-once — handlers must be idempotent) instead of being
///         dispatched in-process after commit.
///     </para>
///     <para>
///         If two boundaries that share the same physical database both enable the outbox, they
///         share a single <c>__EventOutbox</c> table; the atomic claim keeps delivery safe across
///         the two delivery loops.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EnableEventOutboxAttribute : Attribute;

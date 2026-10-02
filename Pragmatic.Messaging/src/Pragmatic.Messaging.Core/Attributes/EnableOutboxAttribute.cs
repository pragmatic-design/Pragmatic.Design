namespace Pragmatic.Messaging.Attributes;

/// <summary>
///     Marks a <c>[Boundary]</c> for the transactional <b>transport-publish</b> outbox: domain
///     events raised by that boundary's entities are captured into <c>__OutboxMessages</c> in the
///     same transaction as the change, then published to the configured transport asynchronously.
/// </summary>
/// <remarks>
///     <para>
///     Requires <c>Pragmatic.Messaging.EFCore</c> (else the attribute is a no-op — the SG reports
///     PRAG0831). The SG maps the outbox table into the boundary's generated DbContext, adds the
///     capture interceptor to its options, and registers the delivery pump and retention purge
///     service — mirroring <c>[EnableSagaPersistence]</c> and the Events <c>[EnableEventOutbox]</c>.
///     </para>
///     <para>
///     A boundary picks <b>one</b> outbox: this one publishes to the transport (cross-service),
///     Events <c>[EnableEventOutbox]</c> dispatches in-process. Both capture and clear the same
///     domain events, so applying both to one boundary is a mistake the SG flags (PRAG0833).
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EnableOutboxAttribute : Attribute;

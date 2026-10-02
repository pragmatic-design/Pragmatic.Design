using Pragmatic.Events;
using Pragmatic.Messaging.Attributes;

namespace Casework.Intake.Events;

/// <summary>
///     Intake asks for a verification of a case. Published; whoever can verify it, verifies it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The namespace is load-bearing, and it is not the project's name.</b> This type lives in
///         <c>Casework.Intake.Contracts</c> but is declared in <c>Casework.Intake.Events</c>, because the
///         transport's topic is derived from the message's namespace — <c>DefaultMessageRouter</c> takes
///         the <b>second</b> segment and publishes to <c>{boundary}.events</c>, so this one goes to
///         <c>intake.events</c>. A project called <c>Casework.Contracts</c> with a matching namespace
///         would have published to <c>contracts.events</c>: a topic named after a build artefact.
///     </para>
///     <para>
///         Why a project of its own: a message is deserialized by its <b>fully qualified type name</b>
///         (<c>IMessageTypeRegistry</c>), so the publisher and the consumer have to name the same type.
///         Two records of the same shape in two assemblies do not meet on the wire. What is shared is
///         therefore this contract, and only this contract: Verify references
///         <c>Casework.Intake.Contracts</c> and never <c>Casework.Intake</c>, which is where the entities
///         and the operations are — asserted by <c>TheContractIsTheOnlyThingShared</c>.
///     </para>
///     <para>
///         <b>It is a domain event of Intake, and that is a decision</b>, not a convenience: in this
///         framework a cross-service contract <em>is</em> the
///         publisher's domain event. Two things follow from it and neither is optional — the writer-side
///         outbox only captures what an entity raised (<c>OutboxInterceptor</c> reads
///         <c>IHasDomainEvents</c>), and the generated AsyncAPI only catalogues types that implement
///         <c>IDomainEvent</c>. A record that is only a message has no transactional guarantee and no
///         place in the document meant to publish it.
///     </para>
///     <para>
///         ⚠️ <c>IIntegrationEvent</c> and not <c>[PublicEvent]</c>: one way of saying one thing. The two
///         are <b>not</b> alternatives — the interface makes the type a domain event <em>and</em> marks
///         it published, the attribute only marks one that already is. Read as alternatives, they give a
///         record marked <c>[PublicEvent]</c> and no published contract at all; <b>PRAG0836</b> reports
///         the attribute applied to a type that is not a domain event.
///     </para>
///     <para>
///         It inherits <c>DomainEvent</c> rather than implementing <c>IDomainEvent</c> by hand, for the
///         <c>EventId</c>: implementing the interface directly leaves it <c>Guid.Empty</c>, and a
///         redelivered outcome is deduplicated on exactly that value.
///     </para>
/// </remarks>
/// <param name="CaseId">The case this verification is about, in Intake's database.</param>
/// <param name="Kind">What is to be verified, as the case asked for it.</param>
/// <param name="Deadline">
///     By when Intake expects an answer. Asked for by the publisher and <b>recorded</b> by the consumer:
///     Verify writes it on its row so an operator of that service can see what it is late against.
///     ⚠️ Verify does not enforce it. What happens when it passes is Intake's business — an hourly job
///     expires the case (<c>ExpireVerificationsJob</c>) — so on this message it is a fact the request
///     carries, not a promise the framework keeps.
/// </param>
/// <param name="OccurredAt">When the case asked. The clock is the application's, not the broker's.</param>
/// <remarks>
///     ⚠️ <b>The tenant is not on it, and must not be.</b> The organisation the request belongs to travels
///     in a transport header (<c>X-Pragmatic-TenantId</c>, written by the outbox row's tenant) and the
///     consumer restores it into the consume scope before the handler runs — so the row Verify writes
///     belongs to the right organisation with nothing in the contract saying so. A tenant in the payload
///     would be a second answer to the same question, and the one a caller could forge.
/// </remarks>
/// <remarks>
///     ⚠️ <c>[CorrelationKey]</c> on the case: it is what routes this message to the saga
///     instance that follows <b>that</b> case, and without it a saga step consuming this type is a build
///     error (<c>PRAG0820</c>). It costs this assembly a reference to <c>Pragmatic.Messaging.Core</c>,
///     where the attribute lives, and that is the right trade: which conversation a message belongs to is
///     a fact about the message, not a secret of whoever happens to orchestrate it. The alternative —
///     <c>ICorrelatedMessage</c> — lives in <c>Pragmatic.Messaging.Saga</c> and would make a contract
///     depend on the existence of sagas.
/// </remarks>
/// <remarks>
///     ⚠️ <c>[PartitionKey]</c> on the same parameter, and the two together are the point: every request
///     about one case carries that case's id in <c>x-partition-key</c>, which is what a partitioned
///     transport orders by. <b>Half of the promise is demonstrated here and half is not</b> — on RabbitMQ
///     the header travels and routes nothing, because ordering per key needs Kafka, which this example
///     does not run. The header is asserted by <c>TheKeyAMessageIsPartitionedBy</c>; the routing is not.
///     <para>
///         Writing it here depends on two things the generator does: it reads the attribute from the
///         property symbol, so the positional form counts, and it scans the assembly that declares the
///         message, because the resolver is needed in the publisher. A scan from the consumer's
///         handlers would leave a contracts assembly with no resolver at all.
///     </para>
/// </remarks>
public sealed record VerificationRequested(
    [property: CorrelationKey, PartitionKey] Guid CaseId,
    string Kind,
    DateTimeOffset Deadline,
    DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt), IIntegrationEvent;

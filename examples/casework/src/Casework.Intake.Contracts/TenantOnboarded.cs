using Pragmatic.Messaging.Attributes;

namespace Casework.Intake.Events;

/// <summary>
///     An organisation has been registered in Intake and Intake's half is ready. Whoever else serves it
///     has to make their own.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The tenant is the subject of this message, not its context.</b> Every other message in
///         this example belongs to a tenant and travels with it in a header; this one is
///         <em>about</em> a tenant that the receiving service does not know yet, so the id is in the
///         payload. A consumer that took it from the header would be asking a question nobody can answer:
///         the organisation does not exist there yet.
///     </para>
///     <para>
///         ⚠️ <b>No connection string.</b> Where an organisation's rows live in Verify is Verify's
///         business, built from Verify's own template — this service does not know it and must not
///         decide it. What crosses is whether the organisation asked for a database of its own, which is
///         a fact about the organisation and not about either database.
///     </para>
///     <para>
///         Not a <c>DomainEvent</c> and not from the outbox: the register is written through
///         <c>ITenantStore</c>, which is SQL on the shared database and not a tracked entity, so there is
///         no transaction for an outbox row to join. That makes the publish non-transactional — the
///         reason the organisation is left <c>Provisioning</c> until the other half answers, instead of
///         being called ready and hoping.
///     </para>
/// </remarks>
public sealed record TenantOnboarded(
    [property: CorrelationKey] string TenantId,
    string Name,
    bool WantsItsOwnDatabase,
    DateTimeOffset OccurredAt);

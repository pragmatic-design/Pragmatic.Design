using Pragmatic.Messaging.Attributes;

namespace Casework.Verify.Events;

/// <summary>
///     Verify has its own database for this organisation and has registered it: its half is done.
/// </summary>
/// <remarks>
///     <para>
///         The second half of the onboarding, answering the first. It is what lets Intake move the
///         organisation from <c>Provisioning</c> to <c>Active</c> — and therefore what makes "the
///         organisation is ready" a fact somebody measured rather than a moment somebody assumed.
///     </para>
///     <para>
///         ⚠️ It carries no detail of how Verify did it. Whether that organisation has a database of its
///         own in Verify, and which, is Verify's business; Intake needs the answer to one question, and
///         a message that carried more would invite Intake to depend on it.
///     </para>
///     <para>
///         There is no <c>TenantNotReady</c>. A failure here is a message that was not handled: it is
///         retried and then dead-lettered, the organisation stays <c>Provisioning</c>, and the request
///         pipeline keeps refusing it. Reporting a failure as a message would need Intake to decide what
///         to do with it, and what a half-onboarded organisation needs is an operator, not a branch.
///     </para>
/// </remarks>
public sealed record TenantReady(
    [property: CorrelationKey] string TenantId,
    DateTimeOffset OccurredAt);

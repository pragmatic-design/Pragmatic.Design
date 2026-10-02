using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when a login attempt fails (wrong password or inactive account).</summary>
/// <remarks>
///     <para>
///         <see cref="Email"/> is plaintext personal data. The value is carried intentionally so that
///         security tooling (audit trails, anomaly detection, account-lockout dashboards) can correlate
///         failed attempts to an account.
///     </para>
///     <para>
///         Whoever persists this event (audit store, outbox) owns its data-protection obligations:
///         apply retention limits and, where GDPR requires it, pseudonymise or hash the address before
///         long-term storage. Do not surface the raw value to unauthenticated callers.
///     </para>
/// </remarks>
public sealed record LoginFailed(string Email, string Reason, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when an account is locked out due to too many failed login attempts.</summary>
public sealed record AccountLocked(string ExternalIdentityKey, DateTimeOffset? LockedUntil, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

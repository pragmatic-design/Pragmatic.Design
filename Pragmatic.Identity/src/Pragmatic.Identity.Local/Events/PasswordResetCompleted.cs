using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when a password reset is successfully confirmed with a valid token.</summary>
public sealed record PasswordResetCompleted(string ExternalIdentityKey, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

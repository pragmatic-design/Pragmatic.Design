using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when a user successfully changes their password.</summary>
public sealed record PasswordChanged(string ExternalIdentityKey, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

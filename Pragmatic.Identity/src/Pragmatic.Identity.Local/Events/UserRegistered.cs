using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when a new user successfully registers with local credentials.</summary>
public sealed record UserRegistered(string ExternalIdentityKey, string Email, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

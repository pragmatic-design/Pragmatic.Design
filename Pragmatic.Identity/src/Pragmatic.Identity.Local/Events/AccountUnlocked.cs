using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>
///     Raised when an account's lockout / failed-attempt state is cleared as a side effect of another
///     operation (e.g. a successful password reset), making the account able to authenticate again.
/// </summary>
public sealed record AccountUnlocked(string ExternalIdentityKey, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

using Pragmatic.Events;

namespace Pragmatic.Identity.Local.Events;

/// <summary>Raised when a user successfully authenticates with local credentials.</summary>
/// <remarks>
///     This event is intentionally network-agnostic: it carries no IP address or User-Agent. Those
///     transport details are not known to the domain action and must not be baked into the event.
///     The host enriches login telemetry with request metadata (IP, UA, geo) via its audit/middleware
///     pipeline when persisting the event.
/// </remarks>
public sealed record UserLoggedIn(string ExternalIdentityKey, DateTimeOffset AuthenticatedAt, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);

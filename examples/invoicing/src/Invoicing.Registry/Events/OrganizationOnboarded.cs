namespace Invoicing.Registry.Events;

/// <summary>
///     A company entered the service: from now on everything its people write belongs to it.
/// </summary>
/// <remarks>
///     Raised at the row's creation — the company exists exactly when its row does, and there is no
///     earlier moment to announce. The slug travels with it because it is the tenant id every other
///     module knows the company by, and a handler has no tenant of its own to look it up in.
/// </remarks>
public sealed record OrganizationOnboarded(
    Guid OrganizationId,
    string Slug,
    string LegalName,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

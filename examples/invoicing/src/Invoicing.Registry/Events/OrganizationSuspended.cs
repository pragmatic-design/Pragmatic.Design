namespace Invoicing.Registry.Events;

/// <summary>
///     A company stopped being served: its people's tokens are refused from the next request, and not
///     one row of its data was touched.
/// </summary>
/// <remarks>
///     Announced because it is the one change to a company that every other part of the system feels,
///     and because the row itself only says <c>State = Suspended</c> — not when, and not that it is a
///     change rather than how the company always was.
/// </remarks>
public sealed record OrganizationSuspended(
    Guid OrganizationId,
    string Slug,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

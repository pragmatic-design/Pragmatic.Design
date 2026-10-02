namespace Invoicing.Registry.Events;

/// <summary>
///     Somebody was added to a company's list of people to bill, and has been given their code.
/// </summary>
/// <remarks>
///     <para>
///         The code travels with it for the same reason <c>OrganizationOnboarded</c> carries the slug: it
///         is what the customer is quoted by — on an invoice, on the telephone — and a handler running
///         after the commit has the event and nothing else.
///     </para>
///     <para>
///         ⚠️ <c>Code</c> is <c>[GeneratedValue("CUS-{SEQ:5}")]</c>, filled by the unit of work on the way
///         into the save, so this event is the one shape that can only be built <b>after</b> it: raised
///         from a create mutation, whose events are constructed after the save so this field arrives
///         filled. <c>TheCodeTheSaveIssues</c> is what holds
///         the ordering, and it is worth a case of its own because nothing about the declaration shows
///         which side of the save it is built on.
///     </para>
/// </remarks>
public sealed record CustomerRegistered(
    Guid CustomerId,
    string Code,
    string Name,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

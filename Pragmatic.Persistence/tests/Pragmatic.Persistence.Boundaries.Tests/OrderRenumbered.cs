using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

/// <summary>An order's new number: the event <see cref="RenumberOrderAction" /> declares, through the Messaging outbox (#736).</summary>
public sealed record OrderRenumbered(Guid Id, string Number, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);

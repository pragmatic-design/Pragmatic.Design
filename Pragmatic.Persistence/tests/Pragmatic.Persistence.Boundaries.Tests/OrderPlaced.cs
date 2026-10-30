using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

public sealed record OrderPlaced(string Number, DateTimeOffset OccurredAt) : IDomainEvent;

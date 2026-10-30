using Pragmatic.Events;

namespace PragmaticBoundaries.Tests;

public sealed record ShipmentSent(string Carrier, DateTimeOffset OccurredAt) : IDomainEvent;

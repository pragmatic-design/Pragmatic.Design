namespace Pragmatic.Messaging.Fuzz;

/// <summary>A message with members of the kinds a payload can get wrong, for the generated registry to read.</summary>
public sealed record FuzzOrderPlaced(
    Guid Id, decimal Amount, DateTimeOffset PlacedAt, int Quantity, DayOfWeek Day, string[] Lines);

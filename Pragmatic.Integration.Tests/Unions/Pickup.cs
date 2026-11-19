namespace Pragmatic.Integration.Tests.Unions;

/// <summary>A shipment collected in store: the other case of <see cref="Shipment" />.</summary>
public sealed record Pickup(string Store);

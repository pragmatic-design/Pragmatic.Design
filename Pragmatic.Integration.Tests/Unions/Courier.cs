namespace Pragmatic.Integration.Tests.Unions;

/// <summary>A shipment by courier: one case of <see cref="Shipment" />.</summary>
public sealed record Courier(string Carrier, int Days);

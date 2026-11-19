namespace Pragmatic.Integration.Tests.Unions;

/// <summary>How an order leaves: a C# 15 union of two shapes, the success value of <see cref="QuoteShipmentAction" />.</summary>
public union Shipment(Courier, Pickup);

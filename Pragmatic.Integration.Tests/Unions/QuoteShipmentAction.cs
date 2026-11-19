using Pragmatic.Actions.Abstractions;
using Pragmatic.Actions.Attributes;
using Pragmatic.Endpoints.Attributes;
using Pragmatic.Result;

namespace Pragmatic.Integration.Tests.Unions;

/// <summary>Quotes how an order leaves, as a union: by courier, or collected in store.</summary>
[DomainAction]
[Endpoint(Pragmatic.Endpoints.HttpVerb.Get, "/api/shipments/quote")]
public partial class QuoteShipmentAction : DomainAction<Shipment>
{
    /// <summary><c>pickup</c> for a collection in store; anything else is a courier.</summary>
    [FromQuery]
    public string Mode { get; init; } = "";

    /// <inheritdoc />
    public override Task<Result<Shipment, IError>> Execute(CancellationToken ct = default)
        => Task.FromResult(Result<Shipment, IError>.Success(
            Mode == "pickup" ? new Shipment(new Pickup("Milano Centro")) : new Shipment(new Courier("DHL", 2))));
}

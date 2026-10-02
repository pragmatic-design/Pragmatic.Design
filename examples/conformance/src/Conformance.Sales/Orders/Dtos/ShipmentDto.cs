using Conformance.Sales.Entities;
using Pragmatic.Mapping.Attributes;

namespace Conformance.Sales.Dtos;

/// <summary>
///     What the reads of a shipment answer.
/// </summary>
[MapFrom<Shipment>]
[GenerateProjection]
public partial record ShipmentDto
{
    public Guid Id { get; init; }

    public string TrackingCode { get; init; } = "";

    public string Carrier { get; init; } = "";
}

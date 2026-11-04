using Pragmatic.Mapping.Attributes;

namespace PragmaticDocuments.Tests;

/// <summary>An order's card: its quantity, and its supplier's name read from the other module's document (<c>[MapProperty("Supplier.Name")]</c>).</summary>
[MapFrom<PartOrder>]
public sealed class PartOrderCard
{
    public int Quantity { get; init; }

    [MapProperty("Supplier.Name")]
    public string? SupplierName { get; init; }
}

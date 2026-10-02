namespace Warehouse.Stock.Contracts.Events;

/// <summary>One picked line: a product by SKU, and how many left the shelf.</summary>
public sealed record PickedLine(string Sku, int Quantity);

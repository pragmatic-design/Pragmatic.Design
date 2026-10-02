namespace Warehouse.Stock.Contracts;

/// <summary>One line to hold: a product by its SKU, and how many.</summary>
public sealed record ReservationLine(string Sku, int Quantity);

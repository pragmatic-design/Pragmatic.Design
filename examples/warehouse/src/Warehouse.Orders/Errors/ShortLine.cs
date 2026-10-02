namespace Warehouse.Orders.Errors;

/// <summary>One line Stock could not hold: what was asked, and what it had.</summary>
public sealed record ShortLine(string Sku, int Requested, int Available);

namespace Warehouse.Stock.Imports.Messages;

/// <summary>
///     One row of a supplier's stock file, as it was written: its line in the file, and the three fields
///     unchecked. Checked by the part that applies it, which is where the catalogue is.
/// </summary>
public sealed record ImportFileRow(int Line, string Sku, string Location, string Quantity);

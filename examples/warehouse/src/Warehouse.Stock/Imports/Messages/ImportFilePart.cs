namespace Warehouse.Stock.Imports.Messages;

/// <summary>
///     One part of an import: a slice of the file's rows, published on the broker so whichever Stock
///     instance is free applies it.
/// </summary>
/// <remarks>
///     Which import it belongs to is the batch id, in the headers the dispatcher sets on every part
///     (<c>BatchTracker.TryGetBatchId</c>): the import's id is its batch's.
/// </remarks>
public sealed record ImportFilePart(int PartIndex, List<ImportFileRow> Rows);

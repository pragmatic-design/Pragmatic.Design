namespace Warehouse.Stock.Imports.Messages;

/// <summary>A supplier's file, already cut into parts: what the batch dispatcher publishes, one part each.</summary>
public sealed record StockImport(List<ImportFilePart> Parts);

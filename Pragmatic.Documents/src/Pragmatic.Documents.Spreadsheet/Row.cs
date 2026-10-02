namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// A row of cells within a worksheet.
/// </summary>
public sealed record Row(IReadOnlyList<Cell> Cells, double? Height = null, bool Hidden = false);

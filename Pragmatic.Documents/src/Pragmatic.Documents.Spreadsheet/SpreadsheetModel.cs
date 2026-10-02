namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// Immutable model representing a spreadsheet workbook with one or more sheets.
/// </summary>
public sealed record SpreadsheetModel
{
    public string? Title { get; init; }
    public string? Author { get; init; }
    public IReadOnlyList<Sheet> Sheets { get; init; } = [];
}

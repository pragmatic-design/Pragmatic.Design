namespace Pragmatic.Documents.Spreadsheet;

/// <summary>
/// A single worksheet within a spreadsheet workbook.
/// </summary>
public sealed record Sheet
{
    public required string Name { get; init; }
    public IReadOnlyList<Column>? Columns { get; init; }
    public IReadOnlyList<Row> Rows { get; init; } = [];
    public FrozenPane? FrozenPane { get; init; }
    public IReadOnlyList<MergeRange>? MergedCells { get; init; }
}

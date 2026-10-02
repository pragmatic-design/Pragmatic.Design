namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Template for a table row.</summary>
public sealed record TableRowTemplate
{
    public IReadOnlyList<TableCellTemplate> Cells { get; init; } = [];
}

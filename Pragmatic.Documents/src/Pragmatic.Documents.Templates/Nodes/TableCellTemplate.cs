namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Template for a table cell — content can contain expressions.</summary>
public sealed record TableCellTemplate
{
    public IReadOnlyList<DocumentNodeTemplate> Content { get; init; } = [];
    public int ColSpan { get; init; } = 1;
    public int RowSpan { get; init; } = 1;
}

namespace Pragmatic.Documents.Model;

/// <summary>Table element with header, body rows, and column definitions.</summary>
public sealed record TableNode : DocumentNode
{
    /// <summary>Column definitions.</summary>
    public IReadOnlyList<TableColumn> Columns { get; init; } = [];

    /// <summary>Header row (optional).</summary>
    public TableRow? Header { get; init; }

    /// <summary>Body rows.</summary>
    public IReadOnlyList<TableRow> Rows { get; init; } = [];

    /// <summary>Whether to repeat header on each page.</summary>
    public bool RepeatHeader { get; init; } = true;
}

using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>
/// Table template — supports static rows OR data-bound rows via DataSource.
/// </summary>
public sealed record TableTemplate : DocumentNodeTemplate
{
    public IReadOnlyList<TableColumn> Columns { get; init; } = [];
    public TableRowTemplate? Header { get; init; }
    public IReadOnlyList<TableRowTemplate> Rows { get; init; } = [];

    /// <summary>Data source path for dynamic rows: <c>"invoice.items"</c>.</summary>
    public string? DataSource { get; init; }

    /// <summary>Row template applied per item when DataSource is set.</summary>
    public TableRowTemplate? RowTemplate { get; init; }

    public bool RepeatHeader { get; init; } = true;
}

using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>
/// Email table template — supports static rows OR data-bound rows via DataSource.
/// </summary>
public sealed record EmailTableTemplate : EmailNodeTemplate
{
    public IReadOnlyList<EmailTableColumn> Columns { get; init; } = [];
    public EmailTableRowTemplate? Header { get; init; }
    public IReadOnlyList<EmailTableRowTemplate> Rows { get; init; } = [];

    /// <summary>Data source for dynamic rows.</summary>
    public string? DataSource { get; init; }

    /// <summary>Row template for each item.</summary>
    public EmailTableRowTemplate? RowTemplate { get; init; }

    public string? BorderColor { get; init; }
    public int CellPadding { get; init; } = 8;
}

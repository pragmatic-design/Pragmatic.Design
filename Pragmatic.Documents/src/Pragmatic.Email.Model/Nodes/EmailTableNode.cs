namespace Pragmatic.Email.Model;

/// <summary>
/// Structured table for email — rendered as email-safe table with inline CSS.
/// No more raw HTML for tabular data.
/// </summary>
public sealed record EmailTableNode : EmailNode
{
    /// <summary>Column definitions.</summary>
    public IReadOnlyList<EmailTableColumn> Columns { get; init; } = [];

    /// <summary>Optional header row (rendered with bold text).</summary>
    public EmailTableRow? Header { get; init; }

    /// <summary>Body rows.</summary>
    public IReadOnlyList<EmailTableRow> Rows { get; init; } = [];

    /// <summary>Border color (hex). Null = no borders.</summary>
    public string? BorderColor { get; init; }

    /// <summary>Cell padding in px.</summary>
    public int CellPadding { get; init; } = 8;
}

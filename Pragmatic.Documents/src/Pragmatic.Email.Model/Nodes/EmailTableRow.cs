namespace Pragmatic.Email.Model;

/// <summary>A row in an email table.</summary>
public sealed record EmailTableRow
{
    /// <summary>Cells in this row.</summary>
    public IReadOnlyList<EmailTableCell> Cells { get; init; } = [];

    /// <summary>Background color for this row (hex).</summary>
    public string? BackgroundColor { get; init; }
}

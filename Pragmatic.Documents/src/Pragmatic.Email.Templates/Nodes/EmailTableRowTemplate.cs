namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Template for an email table row.</summary>
public sealed record EmailTableRowTemplate
{
    public IReadOnlyList<EmailTableCellTemplate> Cells { get; init; } = [];
    public string? BackgroundColor { get; init; }
}

namespace Pragmatic.Documents.Model;

/// <summary>A table row containing cells.</summary>
public sealed record TableRow
{
    public IReadOnlyList<TableCell> Cells { get; init; } = [];
    public NodeStyle? Style { get; init; }
}

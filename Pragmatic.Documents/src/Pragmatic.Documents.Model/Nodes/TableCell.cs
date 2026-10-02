namespace Pragmatic.Documents.Model;

/// <summary>A table cell with content nodes.</summary>
public sealed record TableCell
{
    /// <summary>Cell content (can be any DocumentNode).</summary>
    public IReadOnlyList<DocumentNode> Content { get; init; } = [];

    /// <summary>Column span.</summary>
    public int ColSpan { get; init; } = 1;

    /// <summary>Row span.</summary>
    public int RowSpan { get; init; } = 1;

    public NodeStyle? Style { get; init; }
}

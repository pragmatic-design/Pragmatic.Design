namespace Pragmatic.Documents.Model;

/// <summary>
/// Inline footnote reference. The content appears at the bottom of the page.
/// </summary>
public sealed record FootnoteNode : DocumentNode
{
    /// <summary>Footnote body text.</summary>
    public required string Content { get; init; }
}

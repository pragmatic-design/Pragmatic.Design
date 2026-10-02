namespace Pragmatic.Documents.Model;

/// <summary>
/// Hyperlink to a URL or internal bookmark (#name).
/// </summary>
public sealed record HyperlinkNode : DocumentNode
{
    /// <summary>URL or #bookmark-name.</summary>
    public required string Href { get; init; }

    /// <summary>Inline child nodes (rendered as the link text).</summary>
    public IReadOnlyList<DocumentNode> Children { get; init; } = [];
}

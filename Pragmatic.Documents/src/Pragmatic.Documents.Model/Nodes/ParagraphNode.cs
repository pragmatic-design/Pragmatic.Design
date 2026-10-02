namespace Pragmatic.Documents.Model;

/// <summary>Block-level paragraph containing inline children.</summary>
public sealed record ParagraphNode : DocumentNode
{
    /// <summary>Inline children (TextNode, etc.).</summary>
    public IReadOnlyList<DocumentNode> Children { get; init; } = [];
}

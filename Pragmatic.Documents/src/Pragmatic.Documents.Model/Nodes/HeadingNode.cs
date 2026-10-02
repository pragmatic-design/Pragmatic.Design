namespace Pragmatic.Documents.Model;

/// <summary>Heading (H1-H6).</summary>
public sealed record HeadingNode : DocumentNode
{
    /// <summary>Heading level (1-6).</summary>
    public int Level { get; init; } = 1;

    /// <summary>Plain text content. Used when Children is null.</summary>
    public required string Content { get; init; }

    /// <summary>Optional inline children for mixed formatting within the heading.
    /// When set, these are used instead of Content for rendering.</summary>
    public IReadOnlyList<DocumentNode>? Children { get; init; }
}

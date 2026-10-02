namespace Pragmatic.Documents.Model;

/// <summary>
/// Named anchor for cross-references. Children are rendered normally;
/// the bookmark wraps them as a named destination.
/// </summary>
public sealed record BookmarkNode : DocumentNode
{
    /// <summary>Bookmark name (used in hyperlinks as #name).</summary>
    public required string Name { get; init; }

    /// <summary>Child content nodes wrapped by this bookmark.</summary>
    public IReadOnlyList<DocumentNode> Children { get; init; } = [];
}

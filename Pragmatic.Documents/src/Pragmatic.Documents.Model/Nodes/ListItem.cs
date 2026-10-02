namespace Pragmatic.Documents.Model;

/// <summary>A list item containing content nodes.</summary>
public sealed record ListItem
{
    public IReadOnlyList<DocumentNode> Content { get; init; } = [];

    /// <summary>Optional nested sub-list for multilevel lists.</summary>
    public ListNode? SubList { get; init; }
}

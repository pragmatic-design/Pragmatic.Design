namespace Pragmatic.Documents.Model;

/// <summary>Ordered or unordered list.</summary>
public sealed record ListNode : DocumentNode
{
    /// <summary>Whether the list is ordered (numbered).</summary>
    public bool Ordered { get; init; }

    /// <summary>List items.</summary>
    public IReadOnlyList<ListItem> Items { get; init; } = [];
}

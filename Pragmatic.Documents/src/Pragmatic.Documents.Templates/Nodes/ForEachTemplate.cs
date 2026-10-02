namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>
/// Structural node that repeats children for each item in a collection.
/// </summary>
public sealed record ForEachTemplate : DocumentNodeTemplate
{
    /// <summary>Data source path: <c>"order.items"</c>.</summary>
    public required string DataSource { get; init; }

    /// <summary>Variable name for current item: <c>"item"</c>.</summary>
    public required string ItemName { get; init; }

    /// <summary>Child nodes to repeat per item.</summary>
    public IReadOnlyList<DocumentNodeTemplate> Children { get; init; } = [];
}

namespace Pragmatic.Documents.Model;

/// <summary>Generic container for grouping nodes (e.g. for styling or layout).</summary>
public sealed record ContainerNode : DocumentNode
{
    /// <summary>Child nodes.</summary>
    public IReadOnlyList<DocumentNode> Children { get; init; } = [];
}

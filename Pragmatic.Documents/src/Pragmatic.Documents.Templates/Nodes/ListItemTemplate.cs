namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Template for a list item.</summary>
public sealed record ListItemTemplate
{
    public IReadOnlyList<DocumentNodeTemplate> Content { get; init; } = [];
}

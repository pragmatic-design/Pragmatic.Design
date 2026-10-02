namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Container template with children.</summary>
public sealed record ContainerTemplate : DocumentNodeTemplate
{
    public IReadOnlyList<DocumentNodeTemplate> Children { get; init; } = [];
}

namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Paragraph template with inline children.</summary>
public sealed record ParagraphTemplate : DocumentNodeTemplate
{
    public IReadOnlyList<DocumentNodeTemplate> Children { get; init; } = [];
}

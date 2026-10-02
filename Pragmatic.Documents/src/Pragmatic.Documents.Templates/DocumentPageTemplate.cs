using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.Templates;

/// <summary>
/// A page template within a <see cref="DocumentTemplate"/>.
/// </summary>
public sealed record DocumentPageTemplate
{
    public IReadOnlyList<DocumentNodeTemplate>? Header { get; init; }
    public IReadOnlyList<DocumentNodeTemplate>? Footer { get; init; }
    public IReadOnlyList<DocumentNodeTemplate> Content { get; init; } = [];
}

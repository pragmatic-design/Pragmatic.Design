using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.Templates;

/// <summary>
/// A reusable partial template — a named fragment of document nodes.
/// </summary>
public sealed record DocumentPartialTemplate
{
    public required string Name { get; init; }
    public IReadOnlyList<DocumentNodeTemplate> Content { get; init; } = [];
}

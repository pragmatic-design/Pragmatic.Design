namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>
/// References a reusable partial template by name.
/// Resolved by the <see cref="DocumentTemplateResolver"/> via <see cref="IDocumentPartialProvider"/>.
/// </summary>
public sealed record PartialTemplate : DocumentNodeTemplate
{
    /// <summary>Name of the partial to include.</summary>
    public required string Name { get; init; }
}

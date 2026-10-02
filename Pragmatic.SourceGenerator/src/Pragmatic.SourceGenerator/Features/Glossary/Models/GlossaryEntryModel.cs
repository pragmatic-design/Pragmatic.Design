namespace Pragmatic.SourceGenerator.Features.Glossary.Models;

/// <summary>
///     One entry in the generated ubiquitous-language glossary: an entity (or value object) with its
///     namespace (used as the boundary grouping) and XML-doc summary.
/// </summary>
internal sealed record GlossaryEntryModel
{
    /// <summary>Type name (the domain term).</summary>
    public required string Name { get; init; }

    /// <summary>Containing namespace — used to group terms by boundary/feature.</summary>
    public required string Namespace { get; init; }

    /// <summary>The &lt;summary&gt; XML doc text, or null when undocumented.</summary>
    public string? Summary { get; init; }
}

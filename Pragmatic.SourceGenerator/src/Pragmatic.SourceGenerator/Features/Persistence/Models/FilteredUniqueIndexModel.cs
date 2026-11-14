namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>A <c>[Unique(Filter = …)]</c> index as the context's <c>HasFilter</c> needs it (#729).</summary>
/// <param name="Selector">The lambda selecting the index's columns, as the EntityConfig writes it.</param>
/// <param name="Filter">The predicate as declared, in the Sql engine's syntax.</param>
internal sealed record FilteredUniqueIndexModel(string Selector, string Filter);

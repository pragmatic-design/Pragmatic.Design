namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A to-one include of a row: the navigation, and the object read from its columns (null when the join found nothing).</summary>
internal sealed record IncludeAssignmentModel(string Property, string Hydrate);

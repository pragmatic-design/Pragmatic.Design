namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>An <c>[EagerLoad]</c> path of a query that names no navigation, and its first segment that fails.</summary>
internal sealed record EagerLoadProblemModel(string Path, string Segment);

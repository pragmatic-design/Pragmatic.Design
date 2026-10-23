namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A declared query's sort: its property, the order key it applies, its default direction (-1: none).</summary>
internal sealed record QuerySortModel(string Name, string Key, int Direction);

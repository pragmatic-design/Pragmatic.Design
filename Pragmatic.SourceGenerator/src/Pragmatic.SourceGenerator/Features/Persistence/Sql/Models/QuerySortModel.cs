namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A declared query's sort: its property, the order key it applies, its default direction (-1: none), and where null keys go
///     (<c>[Sort(Nulls = …)]</c>, #733: 0 the database's order, 1 first, 2 last).
/// </summary>
internal sealed record QuerySortModel(string Name, string Key, int Direction, int Nulls = 0);

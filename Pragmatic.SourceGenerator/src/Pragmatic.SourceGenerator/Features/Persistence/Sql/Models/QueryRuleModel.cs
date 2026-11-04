namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A declared query's specification property (a query derived from a specification holds its rule as one): <see cref="Specification" />
///     reads it off the query, and it is ANDed into the query's filters as it is, when <see cref="Guard" /> holds (always when null). Its
///     leaves are compiled where they are written, as every specification's.
/// </summary>
internal sealed record QueryRuleModel(string? Guard, string Specification);

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A declared query's sorts on an engine, in their priority: an ordering of the engine's keys, the entity's key last. Each key is built from
///     its texts as literals (the Sql engine's dialect texts, the documents engine's paths).
/// </summary>
/// <param name="Method">The query's method that returns the ordering (<c>ToSqlOrdering</c>).</param>
/// <param name="Ordering">The engine's ordering type, closed over the entity.</param>
/// <param name="Key">The engine's order key type.</param>
/// <param name="Sorts">The query's sorts, by priority.</param>
/// <param name="Keys">The keys the sorts apply, the entity's key among them.</param>
internal sealed record QueryOrderingModel(string Method, string Ordering, string Key, EquatableArray<QuerySortModel> Sorts, EquatableArray<OrderKeyModel> Keys);

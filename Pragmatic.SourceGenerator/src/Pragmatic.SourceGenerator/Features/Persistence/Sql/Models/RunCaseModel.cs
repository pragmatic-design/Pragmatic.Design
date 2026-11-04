using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A declared query's case in a read repository's <c>RunAsync</c>: the query type, its result (<see cref="Projected" /> when it is not the
///     entity), or why the Sql engine did not compile it (<see cref="Unsupported" />, the case then refuses). A query that maps in memory
///     (<see cref="InMemory" />) reads the entities with <see cref="Includes" /> loaded and runs the result's <c>Selector</c> over them. A query
///     whose result is a grouped <c>[QueryView]</c> reads through <see cref="Grouped" />, its grouping and the view's initializer; one with a
///     <c>[Join&lt;T&gt;]</c> through <see cref="Joined" />, the join and the result's initializer.
/// </summary>
internal sealed record RunCaseModel(string QueryType, string QueryName, string? Unsupported, string ResultType, bool Projected, bool InMemory, EquatableArray<string> Includes, string? Grouped = null, string? Joined = null);

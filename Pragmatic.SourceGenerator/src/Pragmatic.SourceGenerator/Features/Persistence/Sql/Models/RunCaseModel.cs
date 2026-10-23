namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A declared query's case in a read repository's <c>RunAsync</c>: the query type, its result (<see cref="Projected" /> when it is not the
///     entity), or why the Sql engine did not compile it (<see cref="Unsupported" />, the case then refuses).
/// </summary>
internal sealed record RunCaseModel(string QueryType, string QueryName, string? Unsupported, string ResultType, bool Projected);

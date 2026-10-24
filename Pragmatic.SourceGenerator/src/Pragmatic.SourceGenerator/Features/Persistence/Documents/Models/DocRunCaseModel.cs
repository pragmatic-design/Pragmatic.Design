namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>One declared query's case of a documents read repository's <c>RunAsync</c>.</summary>
/// <param name="QueryType">The query's fully qualified type.</param>
/// <param name="QueryName">The query's simple name.</param>
/// <param name="Unsupported">Why the documents engine does not compile it; null when it does.</param>
/// <param name="ResultType">The query's result, fully qualified.</param>
/// <param name="Projection">The <c>.Select(…)</c> of the result, run in .NET; empty when the result is the entity.</param>
internal sealed record DocRunCaseModel(string QueryType, string QueryName, string? Unsupported, string ResultType, string Projection);

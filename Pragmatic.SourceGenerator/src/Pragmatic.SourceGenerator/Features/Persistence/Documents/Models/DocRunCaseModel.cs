using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>One declared query's case of a documents read repository's <c>RunAsync</c>.</summary>
/// <param name="QueryType">The query's fully qualified type.</param>
/// <param name="QueryName">The query's simple name.</param>
/// <param name="Unsupported">Why the documents engine does not compile it; null when it does.</param>
/// <param name="ResultType">The query's result, fully qualified.</param>
/// <param name="Projection">The selector of the result (<c>e =&gt; new R { … }</c>), run in .NET; empty when the result is the entity.</param>
/// <param name="Lookups">
///     The related documents the projection reads a field of (<c>[MapProperty("Nav.Field")]</c>), each read by the keys of the page's documents.
///     With any, the page's documents are read whole and projected in .NET once the related ones are in.
/// </param>
internal sealed record DocRunCaseModel(string QueryType, string QueryName, string? Unsupported, string ResultType, string Projection,
    EquatableArray<DocLookupModel> Lookups);

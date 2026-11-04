using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;

/// <summary>One declared query's case of a documents read repository's <c>RunAsync</c>.</summary>
/// <param name="QueryType">The query's fully qualified type.</param>
/// <param name="QueryName">The query's simple name.</param>
/// <param name="Unsupported">Why the documents engine does not compile it; null when it does.</param>
/// <param name="ResultType">The query's result, fully qualified.</param>
/// <param name="Projection">
///     The selector of the result (<c>e =&gt; new R { … }</c>), run in .NET; empty when the result is the entity; the result's generated
///     <c>Selector</c> when the query maps in memory (<paramref name="InMemory" />).
/// </param>
/// <param name="Lookups">
///     The related documents the projection reads a field of (<c>[MapProperty("Nav.Field")]</c>), or the navigations an in-memory mapping reads, each
///     read by the keys of the page's documents. With any, the page's documents are read whole and projected in .NET once the related ones are in.
/// </param>
/// <param name="InMemory">
///     <c>MapInMemory = true</c> (#505): each lookup's related document is set on its navigation, a document whose required one is missing is left
///     out, and the Selector runs over the rest.
/// </param>
/// <param name="Grouped">
///     A grouped <c>[QueryView]</c> (#507): the view's rows from the matched documents (<c>documents</c>), grouped in .NET; null for any other result.
/// </param>
internal sealed record DocRunCaseModel(string QueryType, string QueryName, string? Unsupported, string ResultType, string Projection,
    EquatableArray<DocLookupModel> Lookups, bool InMemory, string? Grouped = null);

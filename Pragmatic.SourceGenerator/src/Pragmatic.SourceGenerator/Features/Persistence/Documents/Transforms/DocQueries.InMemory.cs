using System.Collections.Generic;
using Pragmatic.SourceGenerator.Features.Persistence.Documents.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A declared query that maps in memory (<c>MapInMemory = true</c>, #505) on the documents engine: the page's documents are read whole, each
///     navigation the result's <c>Selector</c> reads is filled from the related documents read by their keys (<see cref="DocLookupModel" />, as a
///     <c>[MapProperty("Nav.Field")]</c> reads them), and the Selector runs over them, as EF Core's executor runs it over the rows it included. A
///     document keeps a relation as its key, so the navigation is the related document found by that key: one the session's filters hide reads as
///     missing, and a required one missing leaves the document out, as an INNER JOIN would.
/// </summary>
internal static partial class DocQueries
{
    /// <summary>The selector of a query that maps in memory (the result's generated <c>Selector</c>), its navigations' lookups added; null when refused.</summary>
    private static string? InMemory(PragmaticQuery query, DocModel model, Dictionary<string, DocModel> models, List<DocLookupModel> lookups)
    {
        foreach (var navigation in query.InMemoryNavigations)
        {
            if (LookupOf(query, model, models, navigation, lookups, out _, out var why) is null)
            {
                query.Refuse($"MapInMemory reads {navigation}: {why}");
                return null;
            }
        }

        return query.ResultName + ".Selector";
    }
}

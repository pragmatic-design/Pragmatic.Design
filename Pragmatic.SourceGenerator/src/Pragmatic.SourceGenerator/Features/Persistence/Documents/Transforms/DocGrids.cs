using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Documents.Transforms;

/// <summary>
///     A grid adapter's documents half (its fields, its filter and its parsing are <see cref="PragmaticGrid" />'s, engine-neutral): the page read through
///     a <c>DocSession</c>, the sort as an ordering chosen at runtime (<c>DocOrdering</c>, one <c>DocOrderKey</c> per sortable field).
/// </summary>
internal static class DocGrids
{
    /// <summary>The adapter's half the documents engine declares (hint name, text).</summary>
    public static (string Hint, string Source) Source(PragmaticGrid grid, DocModel model)
    {
        var entity = "global::" + grid.Entity.ToDisplayString();
        return grid.SourceText("Pragmatic.Persistence.Documents", "documents engine", "DocGrid",
            new GridLoadModel("global::Pragmatic.Persistence.Documents.DocSession", "global::Pragmatic.Persistence.Documents.DocSpecifications.ToPredicate",
                $"global::Pragmatic.Persistence.Engines.GridPage<{entity}>", CountsInLong: true),
            new GridOrderingModel($"global::Pragmatic.Persistence.Documents.Planning.DocOrdering<{entity}>", "global::Pragmatic.Persistence.Documents.Planning.DocOrderKey",
                // A property the document does not store sorts as a missing field.
                grid.OrderKeyColumns().Select(c => new OrderKeyModel(c, (DocOrderKeys.Texts(model, c) ?? DocOrderKeys.Named(c)).ToEquatableArray())).ToEquatableArray()));
    }
}

using System.Linq;
using Pragmatic.Persistence.Sql.Generator.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>A grid adapter's Sql half: the page read through a <c>SqlSession</c>, the sort as compiled keys per dialect.</summary>
internal sealed partial class PragmaticGrid
{
    /// <summary>The adapter's half the Sql engine declares (hint name, text).</summary>
    public (string Hint, string Source) Source()
        => SourceText("Pragmatic.Persistence.Sql", "Sql engine",
            new GridLoadModel("global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.Pragmatic.SqlSpecifications.ToPredicate", $"global::Pragmatic.Persistence.Sql.Pragmatic.GridPage<{EntityName}>", CountsInLong: false),
            new GridOrderingModel($"global::Pragmatic.Persistence.Sql.SqlOrdering<{EntityName}>", "global::Pragmatic.Persistence.Sql.SqlOrderKey",
                OrderKeyColumns().Select(c => new OrderKeyModel(c, PragmaticQuery.OrderKeyDialects(c).ToEquatableArray())).ToEquatableArray()));
}

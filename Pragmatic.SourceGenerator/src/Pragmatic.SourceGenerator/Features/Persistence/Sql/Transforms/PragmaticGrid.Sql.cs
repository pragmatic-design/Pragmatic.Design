using System.Linq;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>A grid adapter's Sql half: the page read through a <c>SqlSession</c>, the sort as compiled keys per dialect.</summary>
internal sealed partial class PragmaticGrid
{
    /// <summary>The adapter's half the Sql engine declares (hint name, text).</summary>
    public (string Hint, string Source) Source()
        => SourceText("Pragmatic.Persistence.Sql", "Sql engine", "SqlGrid",
            new GridLoadModel("global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.SqlSpecifications.ToPredicate", $"global::Pragmatic.Persistence.Engines.GridPage<{EntityName}>", CountsInLong: false),
            new GridOrderingModel($"global::Pragmatic.Persistence.Sql.Planning.SqlOrdering<{EntityName}>", "global::Pragmatic.Persistence.Sql.Planning.SqlOrderKey",
                OrderKeyColumns().Select(c => new OrderKeyModel(c, PragmaticQuery.OrderKeyDialects(c).ToEquatableArray())).ToEquatableArray()));
}

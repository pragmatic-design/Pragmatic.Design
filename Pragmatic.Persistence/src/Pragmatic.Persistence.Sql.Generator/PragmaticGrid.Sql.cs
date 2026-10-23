namespace Pragmatic.Persistence.Sql.Generator;

/// <summary>A grid adapter's Sql half: the page read through a <c>SqlSession</c>, the sort as compiled keys per dialect.</summary>
internal sealed partial class PragmaticGrid
{
    private const string SqlOrderKey = "global::Pragmatic.Persistence.Sql.SqlOrderKey";

    /// <summary>The adapter's half the Sql engine declares (hint name, text).</summary>
    public (string Hint, string Source) Source() => SourceText("Pragmatic.Persistence.Sql", "Sql engine",
        text => LoadText(text, "global::Pragmatic.Persistence.Sql.SqlSession", "global::Pragmatic.Persistence.Sql.Pragmatic.SqlSpecifications.ToPredicate",
            $"global::Pragmatic.Persistence.Sql.Pragmatic.GridPage<{EntityName}>", countsInLong: false),
        text => OrderingText(text, $"global::Pragmatic.Persistence.Sql.SqlOrdering<{EntityName}>", SqlOrderKey, column => $"new({PragmaticQuery.OrderKeyTexts(column)})"));
}

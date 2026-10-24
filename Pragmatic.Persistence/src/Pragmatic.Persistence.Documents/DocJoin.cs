namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A Join's other side as the generator translated it: the inner chain's filter (written from the inner chain's slots), the outer and inner key
///     paths, the Cosmos DB query of the inner documents of a set of keys (<c>ARRAY_CONTAINS(@__keys, c.key)</c> and the filter) with its
///     parameters, and the slot where the outer chain keeps the inner chain, then the two key selectors and the result selector.
/// </summary>
public sealed class DocJoin<TInner>(DocFilterWriter? filter, string localField, string foreignField, string cosmosText, DocJsonWriter? cosmosParameters, int slot)
    where TInner : IDocumentEntity<TInner>
{
    public DocFilterWriter? Filter { get; } = filter;

    /// <summary>The outer document's key path (<c>$lookup</c>'s localField).</summary>
    public string LocalField { get; } = localField;

    /// <summary>The inner document's key path (<c>$lookup</c>'s foreignField).</summary>
    public string ForeignField { get; } = foreignField;

    public string CosmosText { get; } = cosmosText;

    public DocJsonWriter? CosmosParameters { get; } = cosmosParameters;

    /// <summary>The first of the four slots: the inner chain, the outer key, the inner key, the result.</summary>
    public int Slot { get; } = slot;
}

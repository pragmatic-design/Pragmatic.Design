namespace Pragmatic.Persistence.Documents.Planning;

/// <summary>
///     A Join's other side as the generator translated it: the inner chain's filter (written from the inner chain's slots), the outer and inner key
///     paths, the Cosmos DB query of the inner documents of a set of keys (<c>ARRAY_CONTAINS(@__keys, c.key)</c> and the filter) with its
///     parameters, and the slot where the outer chain keeps the inner chain, then the two key selectors and the result selector. An outer join
///     carries its kind, the Cosmos DB query of every inner document the inner chain selects (a RightJoin and a FullJoin keep those a match does
///     not reach), and the result the generator built, which reads the side that may be missing as null where the caller's would throw.
/// </summary>
public sealed class DocJoin<TInner>(DocFilterWriter? filter, string localField, string foreignField, string cosmosText, DocJsonWriter? cosmosParameters, int slot,
    DocJoinKind kind = DocJoinKind.Inner, string? cosmosAllText = null, Func<object?, object?, object>? result = null)
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

    public DocJoinKind Kind { get; } = kind;

    /// <summary>The inner documents the inner chain selects, every one (its parameters are <see cref="CosmosParameters" />); null on an inner join.</summary>
    public string? CosmosAllText { get; } = cosmosAllText;

    /// <summary>An outer join's row from the outer and the inner document, either of them null where it may be missing; null on an inner join.</summary>
    public Func<object?, object?, object>? Result { get; } = result;
}

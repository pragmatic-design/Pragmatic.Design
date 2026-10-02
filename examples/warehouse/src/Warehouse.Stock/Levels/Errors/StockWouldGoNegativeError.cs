namespace Warehouse.Stock.Errors;

/// <summary>
///     An adjustment refused because it would leave less than nothing on the shelf. Nothing was changed.
/// </summary>
/// <remarks>
///     The words are in <c>translations/*.json</c>, under <c>error.stock.would.go.negative</c>, and the
///     numbers go into them through <see cref="Parameters" />.
/// </remarks>
public sealed partial record StockWouldGoNegativeError : Error
{
    public override string Code => "STOCK_WOULD_GO_NEGATIVE";
    public override int StatusCode => 409;

    /// <summary>What was on hand when the adjustment was asked for.</summary>
    public int OnHand { get; init; }

    /// <summary>The adjustment that was asked for.</summary>
    public int Delta { get; init; }

    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["onHand"] = OnHand,
        ["delta"] = Delta
    };
}

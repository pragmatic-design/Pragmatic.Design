namespace Warehouse.Orders.Errors;

/// <summary>
///     The order was not placed: Stock could not hold every line. Nothing was held for any of them, and
///     the order is still a draft.
/// </summary>
/// <remarks>
///     The short lines travel as data in <see cref="ShortLines" /> and as words through
///     <see cref="Parameters" />, under <c>error.insufficient.stock</c> in <c>translations/*.json</c>.
/// </remarks>
public sealed partial record InsufficientStockError : Error
{
    public override string Code => "INSUFFICIENT_STOCK";
    public override int StatusCode => 409;

    /// <summary>Each line Stock could not hold: its SKU, what was asked, and what was available.</summary>
    public IReadOnlyList<ShortLine> ShortLines { get; init; } = [];

    public override IReadOnlyDictionary<string, object>? Parameters => new Dictionary<string, object>
    {
        ["lines"] = string.Join(", ", ShortLines.Select(line => $"{line.Sku} ({line.Available} of {line.Requested})"))
    };
}

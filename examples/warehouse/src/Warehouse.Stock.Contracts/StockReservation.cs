namespace Warehouse.Stock.Contracts;

/// <summary>
///     Stock's answer: for each line, whether it is held, and how much was available.
/// </summary>
/// <remarks>
///     All or nothing: when <see cref="IsComplete" /> is false, nothing was held for any line — the lines
///     that could have been are not held either.
/// </remarks>
public sealed record StockReservation(IReadOnlyList<ReservedLine> Lines)
{
    public bool IsComplete => Lines.All(line => line.Reserved);
}

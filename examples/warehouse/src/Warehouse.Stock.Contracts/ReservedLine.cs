namespace Warehouse.Stock.Contracts;

/// <summary>
///     One line of the answer: what was asked, what was available across every location, whether it is
///     held, and how much of it is backordered.
/// </summary>
/// <remarks>
///     <see cref="Backordered" /> is non-zero only when Stock accepts backorders (the <c>AcceptBackorders</c>
///     switch): the line is then <see cref="Reserved" /> for what was there, and the rest waits for goods.
/// </remarks>
public sealed record ReservedLine(string Sku, int Requested, int Available, bool Reserved, int Backordered = 0);

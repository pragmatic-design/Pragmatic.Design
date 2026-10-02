namespace Warehouse.Orders.Errors;

/// <summary>
///     Stock did not answer within the time Orders waits for it — or answered that it failed. The order
///     was not placed and is still a draft.
/// </summary>
/// <remarks>
///     A 503 because it is a service being unavailable, not a request being wrong: the same request may
///     succeed a minute later. The customer is told so within the timeout rather than after a hang.
/// </remarks>
public sealed partial record StockUnansweredError : Error
{
    public override string Code => "STOCK_UNANSWERED";
    public override int StatusCode => 503;
}

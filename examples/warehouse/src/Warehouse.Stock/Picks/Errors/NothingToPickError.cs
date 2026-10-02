namespace Warehouse.Stock.Errors;

/// <summary>
///     The order has no confirmed hold to pick: it was never confirmed, its holds expired, or it was
///     already picked. Nothing was changed.
/// </summary>
public sealed partial record NothingToPickError : Error
{
    public override string Code => "NOTHING_TO_PICK";
    public override int StatusCode => 409;
}

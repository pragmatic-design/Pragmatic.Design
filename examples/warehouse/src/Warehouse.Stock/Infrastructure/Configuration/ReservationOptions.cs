namespace Warehouse.Stock.Infrastructure.Configuration;

/// <summary>
///     How long Stock holds an order's stock before giving it back, if nobody confirms the order.
/// </summary>
[Configuration(SectionPath = "Reservations")]
public partial class ReservationOptions
{
    /// <summary>Seconds a hold lasts. Thirty minutes unless configured.</summary>
    [Range(1, 86_400)]
    public int HoldSeconds { get; set; } = 1_800;
}

namespace Pragmatic.Actions.Samples.Entities;

/// <summary>
///     Entity class used by [Query] samples.
/// </summary>
public class Order
{
    public Guid Id { get; set; }
    public string Product { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

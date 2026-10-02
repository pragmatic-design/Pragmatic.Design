namespace Pragmatic.Patch.Samples;

/// <summary>
///     Sample entity with a mix of public setters, private setters with Set methods,
///     and properties that should be excluded from patch generation.
/// </summary>
public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; private set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }

    public void SetStock(int value) => Stock = value;
}

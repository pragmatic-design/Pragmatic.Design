namespace Conformance.Poco.Shapes;

/// <summary>
///     The first-level child, which in turn carries one: it is what makes the shape
///     <b>two deep</b>.
/// </summary>
public sealed class BasketItem
{
    public int Id { get; set; }

    public string Product { get; set; } = "";

    public int Quantity { get; set; }

    /// <summary>The second level. Without it the depth would be one.</summary>
    public List<ItemTag> Tags { get; set; } = [];
}

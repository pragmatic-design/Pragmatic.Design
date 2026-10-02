namespace Conformance.Poco.Shapes;

/// <summary>
///     The root of the <b>two-level 1:N</b> shape, in plain objects.
/// </summary>
/// <remarks>
///     No attributes: it is a POCO. It is not an entity, has no boundary, does not end up in a database.
///     It demonstrates what Mapping does <em>on its own</em>, where the only rules are consistency
///     between objects.
/// </remarks>
public sealed class Basket
{
    public int Id { get; set; }

    public string Label { get; set; } = "";

    public List<BasketItem> Items { get; set; } = [];

    /// <summary>A single navigation, for the one-to-one merge case.</summary>
    public BasketOwner? Owner { get; set; }
}

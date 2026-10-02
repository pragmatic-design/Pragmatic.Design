namespace Conformance.Poco.Shapes;

/// <summary>The shape's second level: a leaf, to stop the recursion.</summary>
public sealed class ItemTag
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

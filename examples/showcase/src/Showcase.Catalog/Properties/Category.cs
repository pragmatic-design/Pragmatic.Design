namespace Showcase.Catalog.Entities;

/// <summary>
/// A hierarchical category for organizing properties (e.g. Resort > Beach Resort > Luxury Beach).
/// Demonstrates [GenerateHierarchy] for recursive CTE queries (GetDescendants, GetAncestors).
/// </summary>
[Entity]
[SoftDelete]
[Lookup]
[Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
[GenerateHierarchy]
public partial class Category : IEntity
{
    [Required]
    [LogicKey]
    [Autocomplete]
    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    /// <summary>Depth level in the hierarchy (0 = root).</summary>
    [DefaultValue(0)]
    public int Level { get; private set; }

    /// <summary>Materialized path for fast subtree queries (e.g. "/root/parent/child").</summary>
    public string Path { get; private set; } = "/";

}

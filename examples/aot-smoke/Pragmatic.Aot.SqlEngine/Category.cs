using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Aot.SqlEngine;

/// <summary>A category in a tree ([GenerateHierarchy] over its optional parent).</summary>
[Entity]
[GenerateHierarchy]
[Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
public partial class Category : IEntity
{
    public string Name { get; set; } = "";
}

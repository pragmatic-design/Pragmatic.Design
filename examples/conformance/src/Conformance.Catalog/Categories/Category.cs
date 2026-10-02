using Pragmatic.Persistence.Entity;

namespace Conformance.Catalog.Entities;

/// <summary>
///     The tree: an entity with an edge to itself.
/// </summary>
/// <remarks>
///     <para>
///         The edge is the declared relation, and <c>[GenerateHierarchy]</c> reads it instead of
///         declaring one of its own: the relation generates <c>ParentId</c> and <c>Parent</c>, and the
///         <c>GetDescendantsByParent</c> / <c>GetAncestorsByParent</c> methods carry the navigation's
///         name.
///     </para>
///     <para>
///         ⚠️ It is the only conformance entity on which the recursive query is <b>executed</b> against a
///         database: the generator snapshots say the method exists, not that the CTE
///         returns rows — <c>TheWholeSubtree</c> measures that.
///     </para>
/// </remarks>
[Entity]
[Relation.ManyToOne<Category>.WithNavigation("Parent", Required = false)]
[GenerateHierarchy]
public partial class Category : IEntity
{
    public string Name { get; private set; } = "";
}

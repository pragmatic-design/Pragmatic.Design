namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One tree on an entity: the self-referencing navigation that is its edge, and the foreign key
///     that edge writes.
/// </summary>
/// <remarks>
///     Both come from the declared <c>[Relation.ManyToOne&lt;TSelf&gt;]</c>, not from a member found by
///     name: the member is generated, and a transform cannot see what another transform emits. The
///     navigation name is also what tells two trees on one entity apart in the generated methods.
/// </remarks>
internal sealed record HierarchyTreeModel(
    string NavigationName,
    string ParentForeignKey);

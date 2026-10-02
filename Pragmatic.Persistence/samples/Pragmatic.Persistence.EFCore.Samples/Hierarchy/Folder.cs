using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Hierarchy;

/// <summary>
///     A self-referencing folder tree. The edge is a declared relation to the type itself, and
///     <c>[GenerateHierarchy]</c> reads it: the SG emits <c>ParentId</c>, <c>Parent</c>, the EF
///     configuration, and <c>FolderHierarchyExtensions</c> with recursive-CTE queries —
///     <c>db.GetDescendantsByParent(rootId)</c> and <c>db.GetAncestorsByParent(childId)</c>.
///     The CTE walks the full subtree (not just direct children), so it needs a relational
///     provider — the sample uses SQLite.
/// </summary>
[Entity]
[Relation.ManyToOne<Folder>.WithNavigation("Parent", Required = false)]
[GenerateHierarchy]
public partial class Folder : IEntity
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    public string Name { get; set; } = "";
}

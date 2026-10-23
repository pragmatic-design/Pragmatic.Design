namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A tree of a Pragmatic entity: the navigation to its parent, and the foreign key column the walk follows.</summary>
internal sealed record HierarchyTreeModel(string Navigation, string ForeignKey);

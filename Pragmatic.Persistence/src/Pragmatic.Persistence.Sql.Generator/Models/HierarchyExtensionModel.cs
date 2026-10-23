namespace Pragmatic.Persistence.Sql.Generator.Models;

/// <summary>A <c>[GenerateHierarchy]</c> walk's session extensions: the descendants and the ancestors of a key, along a navigation, through their plans.</summary>
internal sealed record HierarchyExtensionModel(
    string? Namespace,
    string EntityName,
    string EntityType,
    string Navigation,
    string KeyType,
    string DescendantsPlan,
    string AncestorsPlan) : TrailerSectionModel(Namespace);

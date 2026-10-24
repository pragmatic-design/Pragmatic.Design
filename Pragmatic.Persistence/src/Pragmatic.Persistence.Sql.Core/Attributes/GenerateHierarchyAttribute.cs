namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     On an entity with a navigation to itself (<c>Parent</c>, with <c>ParentId</c>): the generator emits
///     <c>GetDescendantsBy{Navigation}(key)</c> and <c>GetAncestorsBy{Navigation}(key)</c> on <c>SqlSession</c>, one recursive CTE
///     each (the node itself excluded), as the repository's attribute of the same name does. <see cref="Via" /> names the navigation
///     when the entity has several to itself.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class GenerateHierarchyAttribute : Attribute
{
    public string? Via { get; init; }
}

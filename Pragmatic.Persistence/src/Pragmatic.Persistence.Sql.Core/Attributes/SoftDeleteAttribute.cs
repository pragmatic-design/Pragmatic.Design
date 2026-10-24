namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     Rows deleted by marking them (the repository's <c>[SoftDelete]</c>, read by name): the entity declares <c>bool IsDeleted</c> and optionally
///     <c>DeletedAt</c> and <c>DeletedBy</c>. Every read leaves deleted rows out (the chain's, its includes', its subqueries'), unless the chain says
///     <c>IncludeDeleted()</c> or <c>WithoutFilters()</c>; a delete is an UPDATE of those columns (the user from <see cref="SqlContext.Current" />) and
///     <c>RestoreAsync</c> brings the row back. With <see cref="Cascade" /> a graph delete marks the [SoftDelete] rows of its collections too.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SoftDeleteAttribute : Attribute
{
    public bool Cascade { get; set; }
}

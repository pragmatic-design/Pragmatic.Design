namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     How a trait child's create action (add a note, a comment or a tag, upload an attachment) asks for its parent before writing: the parent the
///     route names must be one the caller sees, or the action answers 404 and writes nothing. The reads of a child were filtered by its parent
///     already (#598, <see cref="TraitRowFilters" />); the creation took the route's id as it came, so a caller holding the trait's create
///     permission could write onto another tenant's record, or onto one its row filters hide.
/// </summary>
/// <remarks>
///     On EF Core the parent is read from the boundary's <c>DbContext</c>, which applies the model's filters (tenant, soft delete), and through
///     the parent's row filters when it restricts rows (the <c>_filters</c> provider <see cref="TraitRowFilters" /> declares). On the engines it is
///     read through the parent's read repository, which applies all of them.
/// </remarks>
internal static class TraitParentCheck
{
    /// <summary>The engines' dependency: the parent's read repository.</summary>
    public const string RepositoryField = "_parents";

    /// <summary>The type of <see cref="RepositoryField" /> for <paramref name="parentFullTypeName" />.</summary>
    public static string RepositoryType(string parentFullTypeName)
        => $"global::Pragmatic.Persistence.Repository.IReadRepository<global::{parentFullTypeName}>";

    /// <summary>
    ///     The lines that refuse a parent the caller does not see, before the write: <paramref name="refusal" /> is the statement that answers it,
    ///     <paramref name="parentKey" /> the action's member holding the route's id.
    /// </summary>
    public static string[] GuardLines(bool engine, bool parentIsRowProtected, string parentFullTypeName, string parentKey, string refusal)
    {
        var parent = $"global::{parentFullTypeName}";
        if (engine)
        {
            return
            [
                "// The parent the route names, as the caller sees it: the engine's read applies its tenant, soft delete and row filters.",
                $"if (!await {RepositoryField}.ExistsAsync(global::Pragmatic.Specification.Spec<{parent}>.Where(p => p.PersistenceId == {parentKey}), ct).ConfigureAwait(false))",
                $"    {refusal}",
                "",
            ];
        }

        var lines = new List<string>
        {
            "// The parent the route names, as the caller sees it: the model's filters (tenant, soft delete) hold on the set.",
            $"global::System.Linq.IQueryable<{parent}> __parents = _db.Set<{parent}>();",
        };
        if (parentIsRowProtected)
        {
            lines.Add("// And the parent's row filters, which the model does not carry.");
            lines.Add($"var __visibleParent = _filters.GetCombinedFilter<{parent}>();");
            lines.Add("if (__visibleParent is not null)");
            lines.Add("    __parents = global::System.Linq.Queryable.Where(__parents, __visibleParent);");
        }

        // The key the child's foreign key points at: the parent's PersistenceId (its public Id is not mapped).
        lines.Add($"if (!await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(__parents, p => p.PersistenceId == {parentKey}, ct).ConfigureAwait(false))");
        lines.Add($"    {refusal}");
        lines.Add("");
        return lines.ToArray();
    }
}

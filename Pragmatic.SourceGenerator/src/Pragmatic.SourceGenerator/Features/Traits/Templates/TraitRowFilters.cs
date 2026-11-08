namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     How a trait child's EF Core action reads the child with its parent's visibility (#598): through the row filters the module registers for
///     it (its <c>ParentVisibilityFilter</c>, and the comment's internal visibility), the ones the generated list query and the engines' repositories
///     apply. A raw <c>DbSet</c> applies the model's filters only (tenant, soft delete, moderation).
/// </summary>
/// <remarks>
///     The provider is the action's dependency only where the parent restricts rows: there the module registers a <c>ParentVisibilityFilter</c>
///     for the child, and with it the provider (<c>QueryFilterRegistrationTemplate</c>).
/// </remarks>
internal static class TraitRowFilters
{
    /// <summary>The dependency the action reads the filters from.</summary>
    public const string ProviderType = "global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider";

    /// <summary>The generated method that puts a query of the child through its row filters.</summary>
    public const string HelperName = "WithRowFilters";

    /// <summary>The lines of <see cref="HelperName" /> for <paramref name="childType" />, appended by the action's template inside the class.</summary>
    public static string[] HelperLines(string childType) =>
    [
        "",
        "/// <summary>",
        $"/// The {childType} rows the caller may read: its row filters, its parent's visibility among them, as the engines read it.",
        "/// </summary>",
        $"private global::System.Linq.IQueryable<{childType}> {HelperName}(global::System.Linq.IQueryable<{childType}> rows)",
        "{",
        $"    var visible = _filters.GetCombinedFilter<{childType}>();",
        "    return visible is null ? rows : global::System.Linq.Queryable.Where(rows, visible);",
        "}",
    ];
}

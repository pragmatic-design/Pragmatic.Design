namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     How a comment on EF Core is read with its parent's visibility (#598): through the row filters the module registers for it, as the engines
///     read it through the child's repository. A raw <c>DbSet</c> applies the model's filters only, and a caller who cannot see the parent learned
///     that its comment exists (403 where the engines answer 404).
/// </summary>
internal sealed partial class CommentActionsTemplate
{
    /// <summary>Whether this action reads the comment through the row filters: on EF Core, with a row-protected parent, all but the add.</summary>
    private bool ReadsThroughRowFilters => !_engine && _model.ParentIsRowProtected && _kind != CommentActionKind.Add;

    /// <summary>The comment rows an EF Core read starts from: through the row filters when the parent restricts rows.</summary>
    private string CommentRows => ReadsThroughRowFilters
        ? $"{TraitRowFilters.HelperName}(_db.Set<{_model.CommentTypeName}>())"
        : $"_db.Set<{_model.CommentTypeName}>()";

    private void RenderRowFiltersHelper()
    {
        if (!ReadsThroughRowFilters)
            return;
        foreach (var line in TraitRowFilters.HelperLines(_model.CommentTypeName))
            AppendLine(line);
    }
}

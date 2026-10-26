using Pragmatic.SourceGenerator.Features.Traits.Transforms;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     The bodies of a comment the Sql or documents engine persists: the child's repository instead of EF Core's <c>DbContext</c>. A read by key
///     goes through the engine's filters, so the comment's rules (moderation, internal notes), its soft delete and its parent's visibility and
///     tenancy hold here without being restated, as they do not on a <c>DbSet</c>.
/// </summary>
internal sealed partial class CommentActionsTemplate
{
    private string PendingQueryTypeName => $"ListPending{_model.ParentTypeName}CommentsQuery";

    private void RenderEngineGetByIdBody()
    {
        var dtoName = $"{_model.CommentTypeName}Dto";
        AppendLine("var comment = await _comments.GetByIdAsync(CommentId, ct).ConfigureAwait(false);");
        // Object-level auth: the comment belongs to the parent in the route, not only to the id.
        AppendLine($"if (comment is null || comment.{_model.ParentFkPropertyName} != {_model.ParentFkPropertyName})");
        IncreaseIndent();
        AppendLine($"return Result<{dtoName}, IError>.Failure(NotFoundError.For(\"{_model.CommentTypeName}\", CommentId.ToString()));");
        DecreaseIndent();
        AppendLine($"return new {dtoName}");
        AppendLine("{");
        IncreaseIndent();
        foreach (var property in TraitDtoShape.Comment(_model))
            AppendLine($"{property.Name} = comment.{property.Source},");
        DecreaseIndent();
        AppendLine("};");
    }

    /// <summary>
    ///     The comment by key, tracked by the unit of work the invoker saves. A moderator reaches what the moderation rule hides (a pending comment
    ///     to approve, a rejected one to reinstate): only that rule is lifted, by its name, as every declared rule is on an engine. The soft delete,
    ///     the internal notes and the parent's visibility and tenancy stay.
    /// </summary>
    private void RenderEngineFindComment(bool moderator)
    {
        if (moderator)
        {
            AppendLine("var _toggle = (global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle?)_services.GetService(typeof(global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle));");
            AppendLine($"using var _moderation = _toggle?.DisableQueryFilter(global::Pragmatic.Persistence.Query.Filters.VisibilityFilterName.Of<{_model.CommentTypeName}.{TraitEngineChildren.ModerationRule}>());");
        }

        AppendLine("var comment = await _comments.GetByIdAsync(CommentId, ct).ConfigureAwait(false);");
        AppendLine($"if (comment is null || comment.{_model.ParentFkPropertyName} != {_model.ParentFkPropertyName})");
        IncreaseIndent();
        AppendLine($"return VoidResult<IError>.Failure(NotFoundError.For(\"{_model.CommentTypeName}\", CommentId.ToString()));");
        DecreaseIndent();
        AppendLine();
    }

    /// <summary>
    ///     The moderation queue through the child's declared query of what awaits approval (<c>ListPending{Parent}CommentsQuery</c>, compiled by
    ///     the engine: filtered, ordered and paged in the database), with the moderation rule lifted by its name for the read.
    /// </summary>
    private void RenderEngineListPendingBody()
    {
        var dtoName = $"{_model.CommentTypeName}Dto";
        AppendLine("var _toggle = (global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle?)_services.GetService(typeof(global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle));");
        AppendLine($"using var _moderation = _toggle?.DisableQueryFilter(global::Pragmatic.Persistence.Query.Filters.VisibilityFilterName.Of<{_model.CommentTypeName}.{TraitEngineChildren.ModerationRule}>());");
        AppendLine($"var pending = await _comments.RunAsync(new {PendingQueryTypeName} {{ {_model.ParentFkPropertyName} = {_model.ParentFkPropertyName}, Page = Page < 1 ? 1 : Page, PageSize = PageSize }}, ct).ConfigureAwait(false);");
        AppendLine($"return Result<global::System.Collections.Generic.IReadOnlyList<{dtoName}>, IError>.Success(pending.Items);");
    }
}

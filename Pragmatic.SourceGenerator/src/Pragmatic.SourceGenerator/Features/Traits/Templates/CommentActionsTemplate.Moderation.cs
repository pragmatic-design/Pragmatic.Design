namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>The moderator's two actions: set a comment's status, and list what awaits approval.</summary>
internal sealed partial class CommentActionsTemplate
{
    private void RenderModerateBody()
    {
        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public required Guid CommentId { get; init; }");
        AppendLine("public required CommentStatus NewStatus { get; init; }");
        AppendLine();

        AppendLine("public override async Task<VoidResult<IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            RenderFindComment(ignoreQueryFilters: true);
            AppendLine("comment.Status = NewStatus;");
            AppendLine("comment.UpdatedAt = _clock.UtcNow;");
            AppendLine("comment.UpdatedBy = _currentUser.Id;");
            AppendLine("return Success;");
        });
    }

    /// <summary>
    ///     The moderation queue: the comments on one parent that await approval, oldest first.
    /// </summary>
    /// <remarks>
    ///     The "Moderation" query filter that keeps a pending comment from readers kept it from the
    ///     moderator too, so the approve route existed and nothing listed what to approve. Only that one
    ///     named filter is lifted — tenant and soft delete stay — and the internal-visibility rule is
    ///     applied here, because a DbSet read does not pass through the repository's row filters.
    /// </remarks>
    private void RenderListPendingBody()
    {
        var dtoName = $"{_model.CommentTypeName}Dto";
        var listType = $"global::System.Collections.Generic.IReadOnlyList<{dtoName}>";

        AppendLine($"public required {_model.SimpleIdType} {_model.ParentFkPropertyName} {{ get; init; }}");
        AppendLine("public int Page { get; set; } = 1;");
        // Clamped like the comment list: an unbounded page size let one request pull the whole queue.
        AppendLine("private int _pageSize = 20;");
        AppendLine("public int PageSize { get => _pageSize; set => _pageSize = value is < 1 or > 200 ? 20 : value; }");
        AppendLine();

        AppendLine($"public override async Task<Result<{listType}, IError>> Execute(CancellationToken ct = default)");
        Block(() =>
        {
            var visibility = "";
            if (_model.SupportInternalNotes)
            {
                AppendLine($"var canViewInternal = await _currentUser.Authorization.HasPermissionAsync(\"{ViewInternalPermission}\", ct).ConfigureAwait(false);");
                visibility = " && (canViewInternal || e.Visibility != CommentVisibility.Internal)";
            }

            AppendLine("var page = Page < 1 ? 1 : Page;");
            AppendLine($"{listType} pending = await _db.Set<{_model.CommentTypeName}>()");
            AppendLine("    .IgnoreQueryFilters([\"Moderation\"])");
            AppendLine($"    .Where(e => e.{_model.ParentFkPropertyName} == {_model.ParentFkPropertyName} && !e.IsDeleted && e.Status == CommentStatus.PendingApproval{visibility})");
            AppendLine("    .OrderBy(e => e.CreatedAt)");
            AppendLine("    .Skip((page - 1) * PageSize)");
            AppendLine("    .Take(PageSize)");
            AppendLine($"    .Select({dtoName}.Projection)");
            AppendLine("    .ToListAsync(ct).ConfigureAwait(false);");
            AppendLine($"return Result<{listType}, IError>.Success(pending);");
        });
    }
}

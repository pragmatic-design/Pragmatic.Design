namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     The parent's visibility an attachment action reads through: the row filters of the attachment on EF Core (#598), and for the upload the
///     parent the route names, asked before the file is stored (<see cref="TraitParentCheck" />), so nothing is written for a parent the caller
///     does not see.
/// </summary>
internal sealed partial class AttachmentActionsTemplate
{
    /// <summary>The fields the parent's visibility is read through, in lockstep with <c>TraitActionModelBuilder.BuildAttachmentAction</c>.</summary>
    private void RenderParentVisibilityFields()
    {
        if (!_engine && _model.ParentIsRowProtected)
            AppendLine($"private {TraitRowFilters.ProviderType} _filters = null!;");
        if (_engine && _kind == AttachmentActionKind.Upload)
            AppendLine($"private {TraitParentCheck.RepositoryType(_model.ParentFullTypeName)} {TraitParentCheck.RepositoryField} = null!;");
    }

    /// <summary>The upload's refusal of a parent the caller does not see, before the file is stored.</summary>
    private void RenderParentCheck()
    {
        if (_kind != AttachmentActionKind.Upload)
            return;
        foreach (var line in TraitParentCheck.GuardLines(_engine, _model.ParentIsRowProtected, _model.ParentFullTypeName, _model.ParentFkPropertyName,
                     $"return Result<Guid, IError>.Failure(NotFoundError.For(\"{_model.ParentTypeName}\", {_model.ParentFkPropertyName}.ToString()));"))
            AppendLine(line);
    }
}

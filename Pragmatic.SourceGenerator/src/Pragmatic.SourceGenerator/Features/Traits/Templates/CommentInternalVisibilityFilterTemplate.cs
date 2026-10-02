using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Traits.Models;

namespace Pragmatic.SourceGenerator.Features.Traits.Templates;

/// <summary>
///     Emits <c>InternalVisibilityFilter</c> on a comment entity whose parent declares
///     <c>[HasComments(SupportInternalNotes = true)]</c>: a comment marked <c>Internal</c> is read only by
///     whoever holds <c>{boundary}.{entity}.comments.view-internal</c>.
/// </summary>
/// <remarks>
///     The attribute promised internal notes and nothing enforced them — the permission was never
///     generated and the list never looked at the visibility, so anyone who could read a guest's thread
///     read the staff's notes about the guest. A permission-based row filter is what every generated read
///     through the repository already applies, and what the query cache already partitions by.
///     ⚠️ A read that goes straight to the <c>DbSet</c> does not pass through it: the generated get-by-id
///     checks the visibility itself.
/// </remarks>
internal sealed class CommentInternalVisibilityFilterTemplate : CSharpTemplate
{
    private readonly CommentTraitModel _model;

    public CommentInternalVisibilityFilterTemplate(CommentTraitModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Traits";

    protected override string? SourceInfo =>
        $"Internal visibility filter for {_model.CommentTypeName} (parent {_model.ParentTypeName})";

    protected override bool Validate() => _model.SupportInternalNotes;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.CommentTypeName, "InternalVisibilityFilter", _model.ParentNamespace),
            ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_model.ParentNamespace);
        AppendLine();

        var commentType = $"global::{_model.ParentNamespace}.{_model.CommentTypeName}";
        var permission = TraitPermissions.Slug(
            _model.BoundaryName, _model.ParentTypeName, TraitPermissions.CommentsGroup, TraitPermissions.ViewInternal);

        Class(_model.CommentTypeName, () =>
        {
            XmlSummary(
                $"Hides {_model.CommentTypeName} rows marked Internal. Users with \"{permission}\" bypass it. " +
                "Priority 210.");

            AppendLine(
                "public sealed class InternalVisibilityFilter : " +
                $"global::Pragmatic.Persistence.Query.Filters.IPermissionBasedFilter<{commentType}>");

            Block(() =>
            {
                ExpressionProperty("BypassPermission", "string", $"\"{permission}\"");
                AppendLine();

                ExpressionProperty("Priority", "int", "210");
                AppendLine();

                ExpressionProperty("Scope", "global::Pragmatic.Persistence.Query.Filters.FilterScope",
                    "global::Pragmatic.Persistence.Query.Filters.FilterScope.Default");
                AppendLine();

                XmlSummary("Keeps every comment that is not marked Internal.");
                AppendLine(
                    "public global::System.Linq.Expressions.Expression<global::System.Func<" +
                    $"{commentType}, bool>> GetFilter()");
                IncreaseIndent();
                AppendLine("=> entity => entity.Visibility != global::Pragmatic.Comments.CommentVisibility.Internal;");
                DecreaseIndent();
            });
        },
        modifiers: new ClassModifiers { Partial = true });
    }
}

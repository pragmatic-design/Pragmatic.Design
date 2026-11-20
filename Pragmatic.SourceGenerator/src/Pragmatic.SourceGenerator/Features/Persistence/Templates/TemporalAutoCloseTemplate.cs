using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     <c>AutoClosePreviousAsync</c> (#899): the repository form of <see cref="TemporalValidationTemplate" />'s <c>AutoClosePrevious</c>,
///     part of the entity's shape, so every engine has it.
/// </summary>
/// <remarks>
///     <para>
///         It reads the stretches still open at <c>closedAt</c> through the repository, sets their <c>ValidTo</c>, and hands them back to the
///         repository as updated, for the caller's unit of work to save. The predicate is a specification the shape declares, which every
///         engine compiles (#512).
///     </para>
///     <para>
///         It takes the filter toggle and reads inside the entity's history scope. That is the mistake <c>AutoClosePrevious</c>'s remarks
///         warn of, made impossible: through the default <c>TemporalFilter</c> a stretch starting later than now is invisible, and would be
///         left open beside the new one.
///     </para>
/// </remarks>
internal sealed class TemporalAutoCloseTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalAutoCloseTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"Temporal auto-close for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "TemporalAutoClose", _model.Namespace), ToSourceText());

    // The same gate as AutoClosePrevious (one active stretch at a time), on an IEntity, which a repository is of.
    protected override bool Validate() =>
        _model is { IsValid: true, IsTemporalRelation: true, HasManualEntityInterface: true, TemporalMaxActive: 1 };

    private bool Scoped => _model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null };

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        Class(_model.TypeName, RenderBody, modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var entity = $"global::{_model.FullTypeName}";
        var parentType = TemporalValidationTemplate.ParentFkType(_model);

        XmlSummary("Closes the stretches still open at closedAt, read through the repository inside the history scope: sets their ValidTo and "
                   + "hands them back to the repository as updated. The caller's unit of work saves them.");
        XmlParam("repository", "The repository the stretches are read and updated through.");
        XmlParam("filters", "The request's filter toggle: the history scope is opened through it, so a stretch not active now is closed too.");
        XmlParam("closedAt", "The instant set as ValidTo on each stretch closed.");
        if (Scoped)
            XmlParam("parentId", $"The {_model.TemporalParentTypeName}'s key the stretches belong to.");
        XmlParam("ct", "Cancels the read.");
        XmlReturns("The stretches closed.");

        AppendLine($"public static async global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{entity}>> AutoClosePreviousAsync(");
        AppendLine($"    global::Pragmatic.Persistence.Repository.IRepository<{entity}> repository,");
        AppendLine("    global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle filters,");
        AppendLine("    global::System.DateTimeOffset closedAt,");
        if (Scoped)
            AppendLine($"    {parentType} parentId,");
        AppendLine("    global::System.Threading.CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine($"global::System.Collections.Generic.List<{entity}> open;");
            AppendLine("using (IncludeHistory(filters))");
            AppendLine($"    open = await repository.FindAsync(__TemporalOpenAt({(Scoped ? "parentId, " : "")}closedAt), ct).ConfigureAwait(false);");
            AppendLine();
            AppendLine("foreach (var record in open)");
            Block(() =>
            {
                AppendLine("record.ValidTo = closedAt;");
                AppendLine("repository.Update(record);");
            });
            AppendLine();
            AppendLine("return open;");
        });

        AppendLine();
        var parentParameter = Scoped ? $"{parentType} parentId, " : "";
        var parentCondition = Scoped ? $"e.{_model.TemporalParentFkProperty} == parentId && " : "";
        AppendLine($"private static global::Pragmatic.Specification.Specification<{entity}> __TemporalOpenAt({parentParameter}global::System.DateTimeOffset at)");
        AppendLine($"    => global::Pragmatic.Specification.Spec<{entity}>.Where(e => {parentCondition}(e.ValidTo == null || e.ValidTo > at));");
    }
}

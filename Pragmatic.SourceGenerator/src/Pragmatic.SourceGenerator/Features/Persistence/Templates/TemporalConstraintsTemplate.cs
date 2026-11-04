using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Writes <c>ValidateTemporalConstraintsAsync</c>: the <c>[TemporalRelation]</c> constraints (<c>MaxActive</c>, no overlap) checked
///     against the records already written, read through the repository's specifications. The generated mutation invoker calls it on a
///     create.
/// </summary>
/// <remarks>
///     <para>
///         Part of the entity's shape, for every engine: a specification is what every engine's repository serves, where
///         <see cref="TemporalValidationTemplate" />'s <c>IQueryable</c> is EF Core's only, and the Sql and documents engines compile a
///         <c>Spec&lt;T&gt;.Where</c> only where they see it written, which for a generated one is the shape (#512).
///     </para>
///     <para>
///         The same rules as <see cref="TemporalValidationTemplate" />, read through the same filters: a record the caller cannot see is not
///         counted, on any engine. The predicates take their values as parameters, which every engine reads off a specification.
///     </para>
/// </remarks>
internal sealed class TemporalConstraintsTemplate : CSharpTemplate
{
    private const string Error = "global::Pragmatic.Persistence.Entity.TemporalOverlapError";

    private readonly EntityMetadataModel _model;

    public TemporalConstraintsTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"TemporalConstraints for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "TemporalConstraints", _model.Namespace), ToSourceText());

    // The same gate as TemporalValidationTemplate's, and as the mutation transform's HasTemporalConstraint, which calls the method; and an
    // IEntity, which a repository is of. A mutation's invoker holds an IRepository<T> too, so it compiles for no other entity either.
    protected override bool Validate() =>
        _model is { IsValid: true, IsTemporalRelation: true, HasManualEntityInterface: true } &&
        (_model.TemporalMaxActive > 0 || !_model.TemporalAllowOverlap);

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
        var parent = Scoped ? $"{_model.TemporalParentFkProperty}, " : "";

        XmlSummary("Checks the temporal constraints against the records already written, read through the repository. Null when they hold.");
        XmlParam("existing", "The repository the records are read through, inside the caller's filters.");
        XmlParam("ct", "Cancels the reads.");
        AppendLine($"public async global::System.Threading.Tasks.Task<{Error}?> ValidateTemporalConstraintsAsync(");
        AppendLine($"    global::Pragmatic.Persistence.Repository.IReadRepository<{entity}> existing, global::System.Threading.CancellationToken ct = default)");
        Block(() =>
        {
            if (_model.TemporalMaxActive > 0)
            {
                AppendLine($"var activeCount = await existing.CountAsync(__TemporalActiveAt({parent}ValidFrom), ct).ConfigureAwait(false);");
                AppendLine($"if (activeCount >= {_model.TemporalMaxActive})");
                AppendLine($"    return new {Error} {{ ViolationType = global::Pragmatic.Persistence.Entity.TemporalViolationType.MaxActiveExceeded, MaxActive = {_model.TemporalMaxActive}, CurrentActive = activeCount }};");
                AppendLine();
            }

            if (!_model.TemporalAllowOverlap)
            {
                AppendLine($"var overlapping = ValidTo is {{ }} validTo ? __TemporalOverlapping({parent}ValidFrom, validTo) : __TemporalOverlappingOpen({parent}ValidFrom);");
                AppendLine("if (await existing.ExistsAsync(overlapping, ct).ConfigureAwait(false))");
                AppendLine($"    return new {Error} {{ ViolationType = global::Pragmatic.Persistence.Entity.TemporalViolationType.OverlapDetected }};");
                AppendLine();
            }

            AppendLine("return null;");
        });

        var parentParameter = Scoped ? $"{TemporalValidationTemplate.ParentFkType(_model)} parentId, " : "";
        var parentCondition = Scoped ? $"e.{_model.TemporalParentFkProperty} == parentId && " : "";
        var spec = $"global::Pragmatic.Specification.Specification<{entity}>";
        var where = $"global::Pragmatic.Specification.Spec<{entity}>.Where";
        if (_model.TemporalMaxActive > 0)
        {
            AppendLine();
            AppendLine($"private static {spec} __TemporalActiveAt({parentParameter}global::System.DateTimeOffset from)");
            AppendLine($"    => {where}(e => {parentCondition}e.ValidFrom <= from && (e.ValidTo == null || e.ValidTo > from));");
        }

        if (!_model.TemporalAllowOverlap)
        {
            AppendLine();
            AppendLine($"private static {spec} __TemporalOverlapping({parentParameter}global::System.DateTimeOffset from, global::System.DateTimeOffset to)");
            AppendLine($"    => {where}(e => {parentCondition}e.ValidFrom < to && (e.ValidTo == null || e.ValidTo > from));");
            AppendLine();
            // An open-ended record overlaps every one that has not ended by its start.
            AppendLine($"private static {spec} __TemporalOverlappingOpen({parentParameter}global::System.DateTimeOffset from)");
            AppendLine($"    => {where}(e => {parentCondition}(e.ValidTo == null || e.ValidTo > from));");
        }
    }
}

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates ValidateTemporalConstraints() and AutoClosePrevious() methods
///     for entities with [TemporalRelation] that have MaxActive or AllowOverlap constraints.
/// </summary>
internal sealed class TemporalValidationTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalValidationTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"TemporalValidation for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "TemporalValidation", _model.Namespace),
            ToSourceText());
    }

    // Only generate when there are actual constraints to validate
    protected override bool Validate() =>
        _model is { IsValid: true, IsTemporalRelation: true } &&
        (_model.TemporalMaxActive > 0 || !_model.TemporalAllowOverlap);

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("Pragmatic.Persistence.Entity");
        AddUsing("Pragmatic.Result");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        var entityType = $"global::{_model.FullTypeName}";

        XmlSummary($"Source-generated temporal validation methods for {_model.TypeName}.");

        Class(_model.TypeName, () => RenderBody(entityType),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody(string entityType)
    {
        RenderValidateTemporalConstraints(entityType);

        if (_model.TemporalMaxActive == 1)
        {
            AppendLine();
            RenderAutoClosePrevious(entityType);
        }
    }

    private void RenderValidateTemporalConstraints(string entityType)
    {
        XmlSummary("Validates temporal constraints against existing records. Returns null if valid, or a TemporalOverlapError if violated.");
        XmlParam("existing", "Queryable of existing records to check constraints against.");
        XmlReturns("A TemporalOverlapError if constraints are violated, null otherwise.");

        var parameters = new List<MethodParameter>
        {
            new($"global::System.Linq.IQueryable<{entityType}>", "existing")
        };

        Method("ValidateTemporalConstraints", () => RenderValidateBody(entityType),
            $"global::Pragmatic.Persistence.Entity.TemporalOverlapError?",
            parameters);
    }

    private void RenderValidateBody(string entityType)
    {
        if (_model.TemporalMaxActive > 0)
        {
            // Scope to parent when using typed [TemporalRelation<TParent, TChild>]
            var scopeComment = _model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null }
                ? $" per {_model.TemporalParentTypeName}"
                : "";
            Comment($"Check MaxActive constraint (max {_model.TemporalMaxActive} active at a time{scopeComment})");
            AppendLine("var activeCount = existing");
            IncreaseIndent();
            if (_model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null })
            {
                AppendLine($".Where(e => e.{_model.TemporalParentFkProperty} == {_model.TemporalParentFkProperty})");
            }
            AppendLine(".Where(e => e.ValidFrom <= ValidFrom && (e.ValidTo == null || e.ValidTo > ValidFrom))");
            AppendLine(".Count();");
            DecreaseIndent();
            AppendLine();
            AppendLine($"if (activeCount >= {_model.TemporalMaxActive})");
            Block(() =>
            {
                AppendLine("return new global::Pragmatic.Persistence.Entity.TemporalOverlapError");
                AppendLine("{");
                IncreaseIndent();
                AppendLine("ViolationType = global::Pragmatic.Persistence.Entity.TemporalViolationType.MaxActiveExceeded,");
                AppendLine($"MaxActive = {_model.TemporalMaxActive},");
                AppendLine("CurrentActive = activeCount");
                DecreaseIndent();
                AppendLine("};");
            });
            AppendLine();
        }

        if (!_model.TemporalAllowOverlap)
        {
            Comment("Check for overlapping validity periods");
            AppendLine("var hasOverlap = existing");
            IncreaseIndent();
            if (_model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null })
            {
                AppendLine($".Where(e => e.{_model.TemporalParentFkProperty} == {_model.TemporalParentFkProperty})");
            }
            AppendLine(".Any(e => e.ValidFrom < (ValidTo ?? global::System.DateTimeOffset.MaxValue) && (e.ValidTo == null || e.ValidTo > ValidFrom));");
            DecreaseIndent();
            AppendLine();
            AppendLine("if (hasOverlap)");
            Block(() =>
            {
                AppendLine("return new global::Pragmatic.Persistence.Entity.TemporalOverlapError");
                AppendLine("{");
                IncreaseIndent();
                AppendLine("ViolationType = global::Pragmatic.Persistence.Entity.TemporalViolationType.OverlapDetected");
                DecreaseIndent();
                AppendLine("};");
            });
            AppendLine();
        }

        AppendLine("return null;");
    }

    /// <summary>
    ///     Renders <c>AutoClosePrevious</c>, which closes what is open so the next stretch can start.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         WARNING: what is handed in has to include the history. The method decides what to close
    ///         by comparing ValidTo against the instant being written, and what callers naturally pass
    ///         is repository.Query() - which the generated TemporalFilter has already narrowed to
    ///         whatever is active NOW. A stretch starting tomorrow is invisible to it, and so is one
    ///         that ended yesterday: scheduling a handover ahead of another one closes NOTHING and
    ///         leaves two open stretches on the same parent. Open
    ///         {Entity}.IncludeHistory(filters) around the call.
    ///     </para>
    ///     <para>
    ///         It takes an IQueryable. Taking the repository and the toggle instead would make the
    ///         mistake impossible - but it puts persistence infrastructure into a method on the entity,
    ///         and costs every in-memory test of this helper. It is not needed because MaxActive = 1 is
    ///         a partial unique index: a caller who gets this wrong does not corrupt the history
    ///         silently, because the database refuses the write.
    ///     </para>
    /// </remarks>
    private void RenderAutoClosePrevious(string entityType)
    {
        XmlSummary("Closes the currently active record(s) by setting ValidTo to the specified timestamp. Returns the modified records for persistence.");
        XmlParam("existing", "Existing records to close. Must include stretches that are not active now - see the remarks.");
        XmlParam("closedAt", "The timestamp to set as ValidTo on the previous active record.");

        var parameters = new List<MethodParameter>
        {
            new($"global::System.Linq.IQueryable<{entityType}>", "existing"),
            new("global::System.DateTimeOffset", "closedAt")
        };

        // Add parent FK parameter for typed temporal relations (scoped to parent)
        if (_model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null })
        {
            var fkType = GetParentFkType();
            XmlParam("parentId", $"The {_model.TemporalParentTypeName} ID to scope the auto-close to.");
            parameters.Add(new(fkType, "parentId"));
        }

        Method("AutoClosePrevious", () => RenderAutoCloseBody(),
            $"global::System.Collections.Generic.List<{entityType}>",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderAutoCloseBody()
    {
        AppendLine("var active = existing");
        IncreaseIndent();

        // Scope to parent when using typed [TemporalRelation<TParent, TChild>]
        if (_model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null })
        {
            AppendLine($".Where(e => e.{_model.TemporalParentFkProperty} == parentId)");
        }

        AppendLine(".Where(e => e.ValidTo == null || e.ValidTo > closedAt)");
        AppendLine(".ToList();");
        DecreaseIndent();
        AppendLine();
        AppendLine("foreach (var record in active)");
        Block(() =>
        {
            AppendLine("record.ValidTo = closedAt;");
        });
        AppendLine();
        AppendLine("return active;");
    }

    private string GetParentFkType()
    {
        if (_model.TemporalParentFkProperty is null)
            return "global::System.Guid";

        // The key is generated from the declared relation, so it is among the generated
        // properties, not the source ones.
        var generated = _model.GeneratedRelationProperties.AsImmutableArray()
            .FirstOrDefault(p => p.Kind == RelationPropertyKind.ForeignKey && p.Name == _model.TemporalParentFkProperty);
        var prop = _model.Properties.FirstOrDefault(p => p.Name == _model.TemporalParentFkProperty);
        var typeName = generated?.TypeName ?? prop?.TypeName ?? "System.Guid";
        return typeName.StartsWith("global::") ? typeName : $"global::{typeName}";
    }
}

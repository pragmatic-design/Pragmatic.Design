using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Templates;

/// <summary>
/// Generates {Entity}ErasurePlan — what happens to each classified field when its subject is erased,
/// plus the facts the orchestrator needs to decide about the row as a whole.
/// </summary>
/// <remarks>
/// The plan is generated, not written by hand, because a hand-written one drifts. Someone adds a column
/// six months later, the plan does not know about it, the erasure leaves it behind, and nothing fails —
/// a compliance defect with no signal is the kind that surfaces during an inspection.
/// </remarks>
internal sealed class ErasurePlanTemplate : CSharpTemplate
{
    private readonly PrivacyEntityModel _model;
    private readonly string _typeName;

    public ErasurePlanTemplate(PrivacyEntityModel model)
    {
        _model = model;
        _typeName = NamingHelper.AppendSuffix(model.TypeName, "ErasurePlan");
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Privacy";
    protected override string? TriggerInfo => $"[PersonalData] erasure plan for {_model.TypeName}";

    protected override bool Validate() => _model.HasPersonalData;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_typeName, "PrivacyErase", _model.Namespace), ToSourceText());

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"SG-generated erasure plan for <see cref=\"{_model.TypeName}\"/>.");

        Class(_typeName, RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderBody()
    {
        RenderRowLevelFacts();
        AppendLine();
        RenderRetainedFields();
        AppendLine();
        RenderApply();
    }

    private void RenderRowLevelFacts()
    {
        var deletesRow = false;
        var destroysKey = false;

        foreach (var p in _model.Properties)
        {
            if (p.Classification is not { } c) continue;
            if (c.Erasure == "Delete") deletesRow = true;
            if (c.IsKeyDestruction) destroysKey = true;
        }

        XmlSummary("True when erasing this subject means deleting the whole row, not clearing fields.");
        AppendLine($"public const bool RequiresRowDeletion = {(deletesRow ? "true" : "false")};");
        AppendLine();

        XmlSummary(
            "True when some of this entity's data is erased by destroying the subject's key — which " +
            "also reaches copies no UPDATE can, backups included.");
        AppendLine($"public const bool RequiresKeyDestruction = {(destroysKey ? "true" : "false")};");
    }

    private void RenderRetainedFields()
    {
        XmlSummary(
            "Fields deliberately kept, with the obligation that justifies each. Reported back to the " +
            "subject as part of a partial erasure — an outcome that is legitimate, and has to be stated.");

        // A collection expression, not new[]: retaining nothing is the ordinary case, and an empty
        // implicitly-typed array has no element to infer its type from (CS0826).
        // RequiresKey: a value kept encrypted under the subject's key is unreadable once the key goes,
        // so keeping it keeps the key; a value kept in the clear does not, and must not stop the key's
        // destruction from erasing the fields that rely on it.
        AppendLine("public static IReadOnlyList<(string Field, string Reason, bool RequiresKey)> Retained { get; } =");
        AppendLine("[");
        IncreaseIndent();

        var any = false;
        foreach (var p in _model.Properties)
        {
            if (p.Classification is not { IsRetained: true } c) continue;
            AppendLine($"(\"{p.Name}\", \"{StringHelper.CSharpLiteral(c.Reason ?? string.Empty)}\", {(c.Encrypted ? "true" : "false")}),");
            any = true;
        }

        if (!any)
            AppendLine("// nothing retained");

        DecreaseIndent();
        AppendLine("];");
    }

    private void RenderApply()
    {
        XmlSummary(
            "Applies the per-field strategies to one instance. Row deletion and key destruction are " +
            "not done here: they are decisions about the row and the subject, which the orchestrator " +
            "owns because it also owns the order operations have to happen in.");

        AppendLine($"public static void Apply(global::{_model.FullTypeName} entity, string subjectRef)");
        AppendLine("{");
        IncreaseIndent();

        var wrote = false;
        foreach (var p in _model.Properties)
        {
            if (p.Classification is not { } c) continue;

            switch (c.Erasure)
            {
                case "Null":
                    wrote |= Write(p, "default!", null);
                    break;
                case "Anonymize" when p.AnonymousValue is { } anonymous:
                    wrote |= Write(p, anonymous, "anonymised: no value that identifies anyone");
                    break;
                case "Anonymize":
                    AppendLine($"// {p.Name}: no anonymous value for {p.TypeDisplay} — see PRAG2912");
                    break;
                case "Pseudonymize":
                    wrote |= Write(p, $"({p.TypeDisplay})(object)subjectRef", null);
                    break;
                case "Retain":
                    AppendLine($"// {p.Name} retained: {c.Reason}");
                    break;
                case "Delete":
                case "DestroyKey":
                    // Decided at row / subject level; see the constants above.
                    break;
            }
        }

        if (!wrote)
            AppendLine("// no field-level changes for this entity");

        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Emits the assignment of one field, through whichever way of writing the entity is open.
    /// </summary>
    /// <returns>False when there is none, and nothing was emitted.</returns>
    /// <remarks>
    ///     A Pragmatic entity keeps its setters private and is written through the internal
    ///     <c>Set{Property}</c> that <c>EntitySettersTemplate</c> generates — the same route the mutation
    ///     invoker takes, and the reason the plan does not need a route of its own. Both live in the
    ///     module's own assembly, so <c>internal</c> reaches.
    ///     When neither route exists the field is left alone and PRAG2907 says so: emitting an
    ///     inaccessible assignment would not compile, and emitting silence would drop an erasure without
    ///     a word.
    /// </remarks>
    private bool Write(ClassifiedPropertyModel property, string value, string? note)
    {
        var comment = note is null ? string.Empty : $"   // {note}";

        // Something the entity owns is written through the entity, guarded: an owned reference can be
        // absent — an employee has no identity record until an account is opened — and an unguarded
        // assignment would throw while serving an erasure request. The Set{Property} route below does
        // not apply here: it is generated on the entity, and this property belongs to what it owns.
        if (property.IsNested)
        {
            if (!property.IsPubliclySettable)
            {
                AppendLine($"// {property.Name}: no accessible setter — see PRAG2907");
                return false;
            }

            AppendLine($"if ({property.WriteGuard("entity")})");
            IncreaseIndent();
            AppendLine($"{property.WriteTarget("entity")} = {value};{comment}");
            DecreaseIndent();
            return true;
        }

        if (property.IsPubliclySettable)
        {
            AppendLine($"entity.{property.Name} = {value};{comment}");
            return true;
        }

        if (_model.IsPersistenceEntity)
        {
            // Prefix, not a derived suffix, so NamingHelper does not apply: the name has to be exactly
            // the one EntitySettersTemplate emits.
            AppendLine($"entity.Set{property.Name}({value});{comment}");
            return true;
        }

        AppendLine($"// {property.Name}: no accessible setter — see PRAG2907");
        return false;
    }
}

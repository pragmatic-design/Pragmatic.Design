using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     A temporal relation's reads as specifications (#899): <c>ActiveAt(instant)</c>, and <c>For{Parent}(id)</c> on a typed relation,
///     beside the entity's other specifications (<c>{Entity}Specifications</c>).
/// </summary>
/// <remarks>
///     <para>
///         The repository form of <c>TemporalQueryExtensionsTemplate</c>'s <c>ActiveAt</c> and <c>For{Parent}</c>, which compose onto an
///         <c>IQueryable</c> and so are EF Core's only. A specification is what every engine's repository serves, and the engines compile a
///         generated one because it is part of the entity's shape (#512), as <see cref="TemporalConstraintsTemplate" />'s are.
///     </para>
///     <para>
///         ⚠️ The default <c>TemporalFilter</c> still applies to a read through the repository, so <c>ActiveAt</c> an instant other than now
///         is read inside <c>{Entity}.IncludeHistory(filters)</c>. <c>Active()</c> needs no specification of its own: the filter is it.
///     </para>
/// </remarks>
internal sealed class TemporalSpecificationsTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public TemporalSpecificationsTemplate(EntityMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"Temporal specifications for {_model.TypeName}";
    protected override string? TriggerInfo => $"[TemporalRelation] on {_model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "TemporalSpecifications", _model.Namespace), ToSourceText());

    // An IEntity, which a repository is of, and the specifications class it joins (SpecificationTemplate's gate).
    protected override bool Validate() => _model is { IsValid: true, IsTemporalRelation: true, HasManualEntityInterface: true };

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        // The name SpecificationTemplate gives the class, written the same way, so the two halves are one partial class.
        Class($"{_model.TypeName}Specifications", RenderBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderBody()
    {
        var entity = $"global::{_model.FullTypeName}";
        var spec = $"global::Pragmatic.Specification.Specification<{entity}>";
        var where = $"global::Pragmatic.Specification.Spec<{entity}>.Where";

        XmlSummary($"The {_model.TypeName} records active at <paramref name=\"instant\"/>: begun by then, and not ended. Read the past inside {_model.TypeName}.IncludeHistory(filters).");
        XmlParam("instant", "The instant asked about.");
        AppendLine($"public static {spec} ActiveAt(global::System.DateTimeOffset instant)");
        AppendLine($"    => {where}(e => e.ValidFrom <= instant && (e.ValidTo == null || e.ValidTo > instant));");

        if (_model is { IsTypedTemporalRelation: true, TemporalParentFkProperty: not null, TemporalParentTypeName: not null })
        {
            AppendLine();
            XmlSummary($"The {_model.TypeName} records of one {_model.TemporalParentTypeName}.");
            XmlParam("parentId", $"The {_model.TemporalParentTypeName}'s key.");
            AppendLine($"public static {spec} For{_model.TemporalParentTypeName}({TemporalValidationTemplate.ParentFkType(_model)} parentId)");
            AppendLine($"    => {where}(e => e.{_model.TemporalParentFkProperty} == parentId);");
        }
    }
}

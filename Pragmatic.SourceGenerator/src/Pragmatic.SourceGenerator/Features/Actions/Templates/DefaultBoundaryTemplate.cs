using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Emits the boundary class a module gets when it declares none.
/// </summary>
/// <remarks>
///     Only the declaration: everything that hangs off a boundary — the actions interface, the local
///     implementation, the module metadata — is generated from the same model in the same pass, because
///     nothing in this compilation can discover a type this generator is writing.
/// </remarks>
internal sealed class DefaultBoundaryTemplate : CSharpTemplate
{
    private readonly BoundaryModel _model;

    public DefaultBoundaryTemplate(BoundaryModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"the module of {_model.Namespace}";
    protected override string? TriggerInfo => "[Module] without a [Boundary]";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "Boundary", _model.Namespace), ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, IsGenerated: true };

    public override void RenderFile()
    {
        AddUsing("Pragmatic.Actions.Attributes");
        AppendLine();
        AppendNamespace(_model.Namespace);
        AppendLine();

        XmlSummary(
            $"The boundary of this module. Declared by no one: {_model.Namespace} holds a [Module] and "
            + "no [Boundary], so this one owns every entity and operation the assembly declares. "
            + "Writing a [Boundary] by hand replaces it.");
        AppendLine("[Boundary]");
        Class(_model.TypeName, static () => { },
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });
    }
}

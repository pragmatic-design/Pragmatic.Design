using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Makes every boundary an <c>IBoundary</c>, so an application can configure it.
/// </summary>
/// <remarks>
///     <para>
///         <c>BoundaryConfiguration&lt;TBoundary&gt;</c> — the type <c>UseDatabase</c> hangs off — is
///         constrained to <c>IBoundary</c>, and no generated boundary implemented it. So an application
///         could not write <c>services.AddBoundary&lt;SalesBoundary&gt;(…)</c> for the boundary of its
///         own module, and the DbContext options it would have passed had nowhere to go.
///     </para>
///     <para>
///         A declared <c>[Boundary]</c> is already required to be <c>partial</c>, and the one a module
///         gets without declaring any is emitted <c>partial</c>, so both take this declaration. An
///         author who wrote <c>: IBoundary</c> by hand keeps compiling: an interface repeated across
///         partial declarations is allowed.
///     </para>
/// </remarks>
internal sealed class BoundaryMarkerTemplate : CSharpTemplate
{
    private readonly BoundaryModel _model;

    public BoundaryMarkerTemplate(BoundaryModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Boundary] on {_model.TypeName}";

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "IBoundary", _model.Namespace), ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        // The declared accessibility, repeated: partial declarations that disagree on it do not compile.
        Class(_model.TypeName, static () => { },
            interfaces: ["global::Pragmatic.Actions.Boundary.IBoundary"],
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }
}

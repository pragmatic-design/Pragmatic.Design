using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Writes the <c>Id</c> a mutation loads its row by, when the author declared none.
/// </summary>
/// <remarks>
///     <para>
///         Every mode but Create addresses an existing row, and the invoker addresses it by the
///         mutation's <c>Id</c>. That is implied by the mode, so asking the author to restate it made
///         every Update and Delete in an application carry the same line — and forgetting it produced
///         an operation that compiled, shipped, and found nothing on every call.
///     </para>
///     <para>
///         Declaring your own still wins: this only runs when there is none, so a mutation addressed by
///         something other than the entity's key is unaffected.
///     </para>
/// </remarks>
internal sealed class MutationIdTemplate : CSharpTemplate
{
    private readonly MutationModel _model;

    public MutationIdTemplate(MutationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Mutation(Mode = {_model.Mode})] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Id", _model.Namespace),
        ToSourceText());

    protected override bool Validate() => _model is { IsValid: true, GeneratesIdProperty: true }
                                          && _model.EntityIdTypeName is not null;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        Class(_model.TypeName, RenderBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var entityCref = _model.EntityFullTypeName.Replace("global::", string.Empty);

        var mode = _model.Mode.ToString().ToLowerInvariant();
        var article = "aeiou".IndexOf(mode[0]) >= 0 ? "an" : "a";

        XmlSummary($"The <see cref=\"{entityCref}\"/> this mutation applies to.");
        AppendLine("/// <remarks>");
        AppendLine($"/// Generated: {article} {mode} addresses an existing row, so the id is implied by");
        AppendLine("/// the mode. Declare your own <c>Id</c> to replace it.");
        AppendLine("/// </remarks>");
        AppendLine($"public required {_model.EntityIdTypeName} Id {{ get; init; }}");
    }
}

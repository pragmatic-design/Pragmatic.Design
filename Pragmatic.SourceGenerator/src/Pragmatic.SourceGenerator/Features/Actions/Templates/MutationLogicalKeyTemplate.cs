using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Templates;

/// <summary>
///     Writes <c>{Mutation}.LogicalKey</c>: what a <c>ReturnType = LogicalKey</c> mutation returns.
/// </summary>
/// <remarks>
///     <para>
///         One record, read by both consumers: the boundary member returns it, and the endpoint answers
///         with it. Its <c>From</c> factory is the one projection from the entity, so the in-process
///         answer and the one on the wire cannot carry different parts.
///     </para>
///     <para>
///         The properties are <c>required</c> and <c>init</c>, not positional: the generated JSON context
///         builds a response type without calling a constructor with arguments.
///     </para>
/// </remarks>
internal sealed class MutationLogicalKeyTemplate : CSharpTemplate
{
    /// <summary>The nested record's name, for the templates that name it.</summary>
    public const string RecordName = "LogicalKey";

    private readonly MutationModel _model;

    public MutationLogicalKeyTemplate(MutationModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Actions";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Mutation(ReturnType = LogicalKey)] on {_model.TypeName}";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, RecordName, _model.Namespace),
        ToSourceText());

    protected override bool Validate()
        => _model is { IsValid: true, EffectiveReturnType: MutationReturnTypeValue.LogicalKey };

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

        XmlSummary($"The <c>[LogicKey]</c> of the <see cref=\"{entityCref}\"/> this mutation wrote.");
        AppendLine($"public sealed record {RecordName}");
        Block(() =>
        {
            foreach (var part in _model.LogicalKeyParts)
            {
                // The part travels under the wire name the entity gives it, or the two answers would
                // name the same value differently.
                if (part.JsonName is { } jsonName)
                    AppendLine($"[global::System.Text.Json.Serialization.JsonPropertyName(\"{jsonName}\")]");
                AppendLine($"public required {part.TypeFullName} {part.Name} {{ get; init; }}");
            }

            AppendLine();
            XmlSummary("The key read off the written entity.");
            AppendLine($"public static {RecordName} From({_model.EntityFullTypeName} entity) => new()");
            AppendLine("{");
            IncreaseIndent();
            foreach (var part in _model.LogicalKeyParts)
                AppendLine($"{part.Name} = entity.{part.Name},");
            DecreaseIndent();
            AppendLine("};");
        });
    }
}

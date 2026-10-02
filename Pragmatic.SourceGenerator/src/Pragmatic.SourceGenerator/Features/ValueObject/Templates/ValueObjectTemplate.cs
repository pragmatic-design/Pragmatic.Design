using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.ValueObject.Models;

namespace Pragmatic.SourceGenerator.Features.ValueObject.Templates;

/// <summary>
///     Generates the <c>Create</c> (validate-then-return) and <c>CreateUnsafe</c>
///     (direct construction) factory methods into a [ValueObject] partial record.
/// </summary>
internal sealed class ValueObjectTemplate : CSharpTemplate
{
    private readonly ValueObjectModel _model;

    public ValueObjectTemplate(ValueObjectModel model) => _model = model;

    private bool EmitCreate => _model.HasValidate && !_model.UserDefinedCreate;
    private bool EmitCreateUnsafe => _model.HasConstructor && !_model.UserDefinedCreateUnsafe;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, "ValueObject", _model.Namespace), ToSourceText());

    protected override bool Validate() => _model.IsPartial && (EmitCreate || EmitCreateUnsafe);

    public override void RenderFile()
    {
        if (_model.Namespace is not null)
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        AppendLine($"{_model.Accessibility} partial {_model.TypeKindKeyword} {_model.TypeName}");
        Block(RenderBody);
    }

    private void RenderBody()
    {
        if (EmitCreate)
        {
            var parameters = FormatParameters(_model.ValidateParameters.AsImmutableArray());
            var arguments = FormatArguments(_model.ValidateParameters.AsImmutableArray());
            AppendLine("/// <summary>Validates the inputs and returns the value object or a validation error.</summary>");
            AppendLine($"public static {_model.ValidateReturnType} Create({parameters}) => Validate({arguments});");
        }

        if (EmitCreate && EmitCreateUnsafe)
            AppendLine();

        if (EmitCreateUnsafe)
        {
            var parameters = FormatParameters(_model.ConstructorParameters.AsImmutableArray());
            var arguments = FormatArguments(_model.ConstructorParameters.AsImmutableArray());
            AppendLine("/// <summary>Constructs the value object WITHOUT validation (deserialization / trusted paths only).</summary>");
            AppendLine($"public static {_model.TypeName} CreateUnsafe({parameters}) => new {_model.TypeName}({arguments});");
        }
    }

    private static string FormatParameters(ImmutableArray<ValueObjectParameter> parameters)
        => string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));

    private static string FormatArguments(ImmutableArray<ValueObjectParameter> parameters)
        => string.Join(", ", parameters.Select(p => p.Name));
}

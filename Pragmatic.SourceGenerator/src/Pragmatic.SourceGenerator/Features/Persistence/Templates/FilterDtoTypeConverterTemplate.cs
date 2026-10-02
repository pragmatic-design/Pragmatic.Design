using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Emits a partial class fragment that applies <c>[TypeConverter]</c> to a <c>[FilterDto]</c> class,
///     enabling ASP.NET Core model binding to deserialize the JSON query string parameter automatically.
/// </summary>
/// <remarks>
///     Generated output:
///     <code>
///     [global::System.ComponentModel.TypeConverter(
///         typeof(global::Pragmatic.Persistence.Query.Converters.JsonQueryConverter&lt;global::Ns.MyFilter&gt;))]
///     public partial class MyFilter { }
///     </code>
/// </remarks>
internal sealed class FilterDtoTypeConverterTemplate : CSharpTemplate
{
    private readonly FilterDtoModel _model;

    public FilterDtoTypeConverterTemplate(FilterDtoModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[FilterDto] TypeConverter on {_model.TypeName}";

    public override Artifact RenderOutput() =>
        new(VirtualFolderHints.ForType(_model.TypeName, "TypeConverter", _model.Namespace), ToSourceText());

    protected override bool Validate() => _model.IsValid;

    public override void RenderFile()
    {
        AppendNamespace(_model.Namespace);
        AppendLine();

        var converterType =
            $"global::Pragmatic.Persistence.Query.Converters.JsonQueryConverter<global::{_model.FullTypeName}>";

        AppendLine($"[global::System.ComponentModel.TypeConverter(typeof({converterType}))]");

        Class(_model.TypeName, () => { },
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private static AccessModifier ParseAccessibility(string accessibility) =>
        accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
}

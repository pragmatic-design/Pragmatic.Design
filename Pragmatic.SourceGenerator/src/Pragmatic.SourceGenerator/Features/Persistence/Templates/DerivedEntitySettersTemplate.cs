using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Lightweight setter template for TPH/TPT/TPC derived entity types.
///     Generates only SetX() methods without IChangeTracking infrastructure
///     (which is already declared on the base entity's generated partial).
/// </summary>
/// <remarks>
///     Reached from two places, which between them must cover each derived type exactly once — they
///     write the same hint name, and Roslyn discards the whole generator's output on a duplicate:
///     <c>EntityCoreFeature.GenerateEntitySetters</c> for a derived type that declares
///     <c>[Entity]</c> itself, and <c>PersistenceFeature.GenerateDerivedTypeSetters</c> for one
///     discovered only through its <c>[Inheritance]</c> base.
/// </remarks>
internal sealed class DerivedEntitySettersTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;

    public DerivedEntitySettersTemplate(EntityMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"derived entity type {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Setters", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model is { IsValid: true, HasPrivateSetterProperties: true };
    }

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary(
            $"Internal setters for {_model.TypeName} derived-type properties.");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var props = _model.PrivateSetterProperties.ToList();
        for (var i = 0; i < props.Count; i++)
        {
            RenderSetterMethod(props[i]);
            if (i < props.Count - 1)
                AppendLine();
        }
    }

    private void RenderSetterMethod(PropertyMetadataModel prop)
    {
        var qualifiedType = prop.TypeName.Contains('.') ? $"global::{prop.TypeName}" : prop.TypeName;

        XmlSummary($"Sets the {prop.Name} property.");
        XmlParam("value", $"The new value for {prop.Name}.");

        var parameters = new List<MethodParameter>
        {
            new(qualifiedType, "value")
        };

        Method($"Set{prop.Name}", () =>
        {
            AppendLine($"{prop.Name} = value;");
        }, "void", parameters, AccessModifier.Internal);
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }
}

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

// AggressiveInlining attribute for extension methods
// These are thin wrappers that benefit from inlining

/// <summary>
///     Template for generating extension methods for mapping.
///     Generates To{DtoName}() extension methods for entity and collections.
/// </summary>
internal sealed class MappingExtensionsTemplate : CSharpTemplate
{
    private readonly MappingModel _model;

    public MappingExtensionsTemplate(MappingModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Mapping";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => _model is { HasMapFrom: true, HasMapTo: true } ? $"[MapFrom/MapTo] on {_model.TypeName}" : _model.HasMapFrom ? $"[MapFrom] on {_model.TypeName}" : $"[MapTo] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Extensions", _model.Namespace),
            ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Collections.Generic");
        AddUsing("System.Linq");
        AddUsing("System.Runtime.CompilerServices");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Extensions class - match the DTO accessibility
        var accessibility = TemplateHelpers.ParseAccessibility(_model.Accessibility);
        Class($"{_model.TypeName}MappingExtensions", RenderExtensions,
            accessModifier: accessibility,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderExtensions()
    {
        RenderSingleExtension();
        RenderEnumerableExtension();
        RenderListExtension();
        RenderArrayExtension();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Single Entity Extension
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderSingleExtension()
    {
        XmlSummary($"Maps a {_model.SourceTypeName} entity to a {_model.TypeName}.");
        XmlParam("entity", "The source entity to map.");
        XmlReturns($"A new {_model.TypeName} instance.");

        var parameters = new List<MethodParameter>
        {
            new($"global::{_model.SourceTypeFullName}", "entity") { IsExtension = true }
        };

        ExpressionMethod($"To{_model.TypeName}",
            $"{_model.TypeName}.FromEntity(entity)",
            _model.TypeName,
            parameters,
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // IEnumerable Extension
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderEnumerableExtension()
    {
        XmlSummary($"Maps a sequence of {_model.SourceTypeName} entities to {_model.TypeName}.");
        XmlParam("entities", "The source entities to map.");
        XmlReturns($"A sequence of {_model.TypeName} instances.");

        var parameters = new List<MethodParameter>
        {
            new($"IEnumerable<global::{_model.SourceTypeFullName}>", "entities") { IsExtension = true }
        };

        // Use lambda syntax when HasCircularReferences because FromEntity has optional visited parameter
        var selectExpr = _model.HasCircularReferences
            ? $"entities.Select(e => {_model.TypeName}.FromEntity(e))"
            : $"entities.Select({_model.TypeName}.FromEntity)";

        ExpressionMethod($"To{_model.TypeName}",
            selectExpr,
            $"IEnumerable<{_model.TypeName}>",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // List Extension
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderListExtension()
    {
        XmlSummary($"Maps a list of {_model.SourceTypeName} entities to {_model.TypeName}.");
        XmlParam("entities", "The source entities to map.");
        XmlReturns($"A list of {_model.TypeName} instances.");

        var parameters = new List<MethodParameter>
        {
            new($"List<global::{_model.SourceTypeFullName}>", "entities") { IsExtension = true }
        };

        // Use lambda syntax when HasCircularReferences because FromEntity has optional visited parameter
        var convertExpr = _model.HasCircularReferences
            ? $"entities.ConvertAll(e => {_model.TypeName}.FromEntity(e))"
            : $"entities.ConvertAll({_model.TypeName}.FromEntity)";

        ExpressionMethod($"To{_model.TypeName}",
            convertExpr,
            $"List<{_model.TypeName}>",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");

        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Array Extension
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderArrayExtension()
    {
        XmlSummary($"Maps an array of {_model.SourceTypeName} entities to {_model.TypeName}.");
        XmlParam("entities", "The source entities to map.");
        XmlReturns($"An array of {_model.TypeName} instances.");

        var parameters = new List<MethodParameter>
        {
            new($"global::{_model.SourceTypeFullName}[]", "entities") { IsExtension = true }
        };

        // Use lambda syntax when HasCircularReferences because FromEntity has optional visited parameter
        var convertExpr = _model.HasCircularReferences
            ? $"Array.ConvertAll(entities, e => {_model.TypeName}.FromEntity(e))"
            : $"Array.ConvertAll(entities, {_model.TypeName}.FromEntity)";

        ExpressionMethod($"To{_model.TypeName}",
            convertExpr,
            $"{_model.TypeName}[]",
            parameters,
            modifiers: new MethodModifiers { IsStatic = true },
            attribute: "MethodImpl(MethodImplOptions.AggressiveInlining)");
    }
}

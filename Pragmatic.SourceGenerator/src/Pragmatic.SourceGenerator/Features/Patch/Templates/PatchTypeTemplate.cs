using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Patch.Models;

namespace Pragmatic.SourceGenerator.Features.Patch.Templates;

/// <summary>
///     Generates the partial record body for a patch DTO:
///     Optional&lt;T&gt; properties, ApplyTo(entity), ModifiedProperties.
/// </summary>
internal sealed class PatchTypeTemplate : CSharpTemplate
{
    private readonly PatchModel _model;

    public PatchTypeTemplate(PatchModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Patch";
    protected override string? SourceInfo => $"{_model.TypeName} patching {_model.EntityName}";
    protected override string? TriggerInfo => $"[GeneratePatch<{_model.EntityName}>] on {_model.TypeName}";

    protected override bool Validate() => _model.IsValid;

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "Patch", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsings("System", "System.Collections.Generic", "Pragmatic.Patch");

        AppendNamespace(_model.Namespace);
        AppendLine();

        Record(_model.TypeName, RenderBody,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        RenderProperties();
        AppendLine();
        RenderApplyTo();
        AppendLine();
        RenderModifiedProperties();
    }

    private void RenderProperties()
    {
        Comment("═══ Optional<T> patch properties ═══");
        AppendLine();

        foreach (var prop in _model.Properties)
        {
            XmlSummary($"Patch value for {_model.EntityName}.{prop.Name}. {StatesOf(prop)}");
            AppendLine($"public Optional<{prop.TypeFullName}> {prop.Name} {{ get; init; }}");
            AppendLine();
        }
    }

    /// <summary>The states this property actually has, written where the author reads them.</summary>
    /// <remarks>
    ///     ⚠️ Not the same sentence on every property — "Undefined = don't change, Null = clear,
    ///     Value = set" — because on a column that cannot be null the promise would be in a comment and
    ///     the refusal would come from the database: an author following the documentation would send
    ///     <c>{"field": null}</c> and get a constraint violation. A tri-state is a tri-state only where
    ///     there are three states.
    /// </remarks>
    private static string StatesOf(PatchPropertyModel prop)
    {
        if (prop.IsNullable)
            return "Undefined = don't change, Null = clear, Value = set.";

        return prop.IsValueType
            ? "Undefined = don't change, Value = set. A JSON null reads as undefined: the column cannot be null."
            : "Undefined = don't change, Value = set. A JSON null is refused: the column cannot be null.";
    }

    private void RenderApplyTo()
    {
        XmlSummary($"Applies all present (HasValue) fields to the given {_model.EntityName} entity.");
        XmlParam("entity", "The entity to apply changes to.");

        Method("ApplyTo", RenderApplyToBody, "void",
            new List<MethodParameter> { new(_model.EntityFullName, "entity") });
    }

    private void RenderApplyToBody()
    {
        foreach (var prop in _model.Properties)
        {
            // Non-nullable reference types: .Value returns T? but target is T — needs null-forgiving
            var valueSuffix = (!prop.IsNullable && !prop.IsValueType) ? "!" : "";

            If($"{prop.Name}.HasValue", () =>
            {
                if (prop.HasSetMethod)
                    AppendLine($"entity.Set{prop.Name}({prop.Name}.Value{valueSuffix});");
                else
                    AppendLine($"entity.{prop.Name} = {prop.Name}.Value{valueSuffix};");
            });
        }
    }

    private void RenderModifiedProperties()
    {
        XmlSummary("Gets the set of property names that were present in the input (HasValue = true).");

        ExpressionProperty("ModifiedProperties", "IReadOnlySet<string>", "GetModifiedProperties()");
        AppendLine();

        Method("GetModifiedProperties", RenderGetModifiedPropertiesBody, "HashSet<string>",
            accessModifier: AccessModifier.Private);
    }

    private void RenderGetModifiedPropertiesBody()
    {
        AppendLine("var set = new HashSet<string>(StringComparer.Ordinal);");

        foreach (var prop in _model.Properties)
            If($"{prop.Name}.HasValue", () => { AppendLine($"set.Add(\"{prop.Name}\");"); });

        Return("set");
    }
}

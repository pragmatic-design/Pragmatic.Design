using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating ApplyPatch method and _setProperties tracking on patch DTOs.
///     Generates a partial class with property tracking and a sync ApplyPatch method.
/// </summary>
internal sealed partial class PatchApplyTemplate : Mapping.Templates.ChildWritingTemplate
{
    private readonly MutationMetadataModel _model;

    public PatchApplyTemplate(MutationMetadataModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Patch] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Patch", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("System.Collections.Generic");

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        XmlSummary($"Generated patch support for {_model.TypeName}.");

        Class(_model.TypeName, RenderBody,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderBody()
    {
        var entityType = $"global::{_model.EntityFullTypeName}";

        // _setProperties field and accessors
        RenderSetPropertiesTracking();

        AppendLine();

        // ApplyPatch method
        RenderApplyPatchMethod(entityType);

        // What a caller has to load before applying it.
        RenderWrittenNavigations();
    }

    private void RenderSetPropertiesTracking()
    {
        AppendLine($"private readonly global::System.Collections.Generic.HashSet<string> _setProperties = new({_model.Properties.Length});");
        AppendLine();

        XmlSummary("Gets the set of property names that have been explicitly marked for patching.");
        AppendLine("public global::System.Collections.Generic.IReadOnlySet<string> SetProperties => _setProperties;");
        AppendLine();

        XmlSummary("Marks a property as explicitly set, so it will be applied during <see cref=\"ApplyPatch\"/>.");
        XmlParam("propertyName", "The name of the property to mark as set.");
        AppendLine("public void MarkSet(string propertyName) => _setProperties.Add(propertyName);");
    }

    private void RenderApplyPatchMethod(string entityType)
    {
        XmlSummary($"Applies the set properties from this patch to the target {_model.EntityTypeName} entity.");
        XmlParam("target", "The entity to apply changes to.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "target")
        };

        Method("ApplyPatch", () => RenderApplyPatchBody(entityType), "void", parameters, AccessModifier.Public);
    }

    private void RenderApplyPatchBody(string entityType)
    {
        // Get applicable properties (not ignored, exists on entity, carrying a value rather than a child)
        var applicableProps = _model.Properties
            .Where(p => !p.IsIgnored
                && p is { EntityPropertyExists: true, IsCollection: false, IsNestedMutation: false, RelatedCanCreate: false })
            .ToList();

        // Children a patch can actually write: a collection whose elements can be matched, or a
        // nested DTO that knows how to create or update the entity behind it. The rest stay out —
        // and say so, through PRAG2203/PRAG2204, rather than disappearing.
        var relatedProps = _model.Properties
            .Where(p => !p.IsIgnored && p.EntityPropertyExists && IsRelated(p) && CanWrite(p))
            .ToList();

        if (applicableProps.Count == 0 && relatedProps.Count == 0)
        {
            Comment("No applicable properties to patch.");
            return;
        }

        // If DTO has [MapTo<T>], delegate simple assignments but still respect _setProperties
        if (_model.HasMapToAttribute)
        {
            Comment("Delegate to Mapping-generated ApplyTo() when all properties are set.");
            AppendLine("if (_setProperties.Count == 0)");
            Block(() =>
            {
                AppendLine("ApplyTo(target);");
                AppendLine("return;");
            });
            AppendLine();
        }

        // When _setProperties is populated, use explicit tracking
        AppendLine("if (_setProperties.Count > 0)");
        Block(() =>
        {
            foreach (var prop in applicableProps)
                RenderTrackedPropertyAssignment(prop);

            foreach (var prop in relatedProps)
            {
                AppendLine($"if (_setProperties.Contains(nameof({prop.PropertyName})))");
                Block(() => RenderRelatedWrite(prop));
            }
        });

        // Fallback: when _setProperties is empty, use nullable check (backward compat)
        AppendLine("else");
        Block(() =>
        {
            foreach (var prop in applicableProps)
                RenderNullablePropertyAssignment(prop);

            foreach (var prop in relatedProps)
            {
                AppendLine($"if ({prop.PropertyName} is not null)");
                Block(() => RenderRelatedWrite(prop));
            }
        });
    }

    /// <summary>
    ///     Renders property assignment guarded by _setProperties.Contains().
    ///     Uses null-forgiving (!) because the user explicitly marked the property as set.
    /// </summary>
    private void RenderTrackedPropertyAssignment(MutationPropertyModel prop)
    {
        var targetProp = prop.TargetPropertyName;
        var sourceProp = prop.PropertyName;
        // Null-forgiving operator for nullable properties (user explicitly set the value)
        var nullForgiving = prop.IsNullable ? "!" : "";
        var valueAccess = NeedsValueUnwrap(prop) ? $"{nullForgiving}.Value" : nullForgiving;

        AppendLine($"if (_setProperties.Contains(nameof({sourceProp})))");

        if (prop.HasConverter)
        {
            Block(() =>
            {
                var converterType = $"global::{prop.ConverterType}";
                AppendLine($"var converter = new {converterType}();");
                var converterValueAccess = prop is { IsNullable: true, IsValueType: true } ? "!.Value" : "!";
                var assignment = prop.EntityHasPrivateSetter
                    ? $"target.Set{targetProp}(converter.ConvertBack({sourceProp}{converterValueAccess}))"
                    : $"target.{targetProp} = converter.ConvertBack({sourceProp}{converterValueAccess})";
                AppendLine($"{assignment};");
            });
        }
        else
        {
            var assignment = prop.EntityHasPrivateSetter
                ? $"target.Set{targetProp}({sourceProp}{valueAccess})"
                : $"target.{targetProp} = {sourceProp}{valueAccess}";
            Block(() => AppendLine($"{assignment};"));
        }
    }

    /// <summary>
    ///     Renders property assignment guarded by nullable check (fallback when _setProperties is empty).
    ///     Uses null-forgiving (!) because C# doesn't narrow properties through null checks.
    /// </summary>
    private void RenderNullablePropertyAssignment(MutationPropertyModel prop)
    {
        var targetProp = prop.TargetPropertyName;
        var sourceProp = prop.PropertyName;

        if (prop.IsNullable)
        {
            // Null-forgiving needed because C# properties are not narrowed by `is not null` checks
            var valueAccess = NeedsValueUnwrap(prop) ? "!.Value" : "!";

            AppendLine($"if ({sourceProp} is not null)");

            if (prop.HasConverter)
            {
                Block(() =>
                {
                    var converterType = $"global::{prop.ConverterType}";
                    AppendLine($"var converter = new {converterType}();");
                    var converterValueAccess = prop.IsValueType ? "!.Value" : "!";
                    var assignment = prop.EntityHasPrivateSetter
                        ? $"target.Set{targetProp}(converter.ConvertBack({sourceProp}{converterValueAccess}))"
                        : $"target.{targetProp} = converter.ConvertBack({sourceProp}{converterValueAccess})";
                    AppendLine($"{assignment};");
                });
            }
            else
            {
                var assignment = prop.EntityHasPrivateSetter
                    ? $"target.Set{targetProp}({sourceProp}{valueAccess})"
                    : $"target.{targetProp} = {sourceProp}{valueAccess}";
                Block(() => AppendLine($"{assignment};"));
            }
        }
        else
        {
            // Non-nullable: always apply
            if (prop.HasConverter)
            {
                var converterType = $"global::{prop.ConverterType}";
                var assignment = prop.EntityHasPrivateSetter
                    ? $"target.Set{targetProp}(new {converterType}().ConvertBack({sourceProp}))"
                    : $"target.{targetProp} = new {converterType}().ConvertBack({sourceProp})";
                AppendLine($"{assignment};");
            }
            else
            {
                var assignment = prop.EntityHasPrivateSetter
                    ? $"target.Set{targetProp}({sourceProp})"
                    : $"target.{targetProp} = {sourceProp}";
                AppendLine($"{assignment};");
            }
        }
    }

    private static bool NeedsValueUnwrap(MutationPropertyModel prop)
    {
        if (!prop.IsNullable || !prop.IsValueType)
            return false;

        if (string.IsNullOrEmpty(prop.EntityPropertyType))
            return false;

        var entityType = prop.EntityPropertyType!;
        var isEntityNullable = entityType.EndsWith("?")
            || entityType.Contains("System.Nullable<")
            || entityType.Contains("Nullable<");

        return !isEntityNullable;
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

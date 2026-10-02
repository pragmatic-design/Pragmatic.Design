using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Mapping.Models;

namespace Pragmatic.SourceGenerator.Features.Mapping.Templates;

/// <summary>
///     FromEntity generation: creates DTO from entity, partial methods, context struct.
/// </summary>
internal sealed partial class MappingTemplate
{
    // ═══════════════════════════════════════════════════════════════════════════
    // FromEntity Generation
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderFromEntity()
    {
        XmlSummary($"Creates a new {_model.TypeName} from the specified {_model.SourceTypeName} entity.");
        XmlParam("entity", "The source entity to map from.");
        if (_model.HasCircularReferences)
            XmlParam("visited", "Set of already visited instances for circular reference tracking.");
        XmlReturns($"A new {_model.TypeName} instance.");

        var parameters = new List<MethodParameter>
        {
            new("global::" + _model.SourceTypeFullName, "entity")
        };

        if (_model.HasCircularReferences)
            parameters.Add(new MethodParameter("HashSet<object>", "visited", true)
            {
                DefaultValue = "null"
            });

        Method("FromEntity", RenderFromEntityBody, _model.TypeName, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderFromEntityBody()
    {
        // Skip null check for value types (structs) - they can't be null
        if (!_model.SourceTypeIsValueType)
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(entity);");
            AppendLine();
        }

        // Polymorphic dispatch ([MapDerived]) — declaration order, most-derived first.
        if (_model.DerivedMappings.Length > 0)
        {
            AppendLine("switch (entity)");
            AppendLine("{");
            IncreaseIndent();
            var d = 0;
            foreach (var derived in _model.DerivedMappings)
            {
                AppendLine($"case {derived.SourceTypeExpr} __derived{d}: return {derived.DtoTypeExpr}.FromEntity(__derived{d});");
                d++;
            }
            DecreaseIndent();
            AppendLine("}");
            AppendLine();
        }

        if (_model.HasCircularReferences)
        {
            AppendLine("visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);");
            AppendLine();
            If("!visited.Add(entity)", () => { Return("default!"); });
            AppendLine();
        }

        // BeforeMapping hook
        AppendLine($"{_model.TypeName}? customResult = null;");
        AppendLine("BeforeMapping(entity, ref customResult);");
        If("customResult is not null", () =>
        {
            // For value types (struct, record struct), we need .Value
            var returnExpr = _model.IsValueType ? "customResult.Value" : "customResult";
            Return(returnExpr);
        });
        AppendLine();

        // Build context struct
        AppendLine($"var ctx = new {_model.TypeName}MappingContext");
        Block(() =>
        {
            foreach (var prop in _model.Properties.Where(p => !p.IsIgnored && p.Resolution != MappingResolution.None))
            {
                var expr = GenerateMappingExpression(prop);
                // [MapCondition]: gate the mapping behind the user predicate; default when false.
                if (prop is { ConditionMethod: not null, ConditionMethodInvalid: false })
                    expr = $"{prop.ConditionMethod}(entity) ? ({expr}) : default!";
                AppendLine($"{prop.PropertyName} = {expr},");
            }
        });
        AppendLine(";");
        AppendLine();

        // CustomizeMapping hook
        AppendLine("CustomizeMapping(entity, ref ctx);");
        AppendLine();

        // Create result — values come from the context struct built above.
        var mapped = _model.Properties.Where(p => !p.IsIgnored && p.Resolution != MappingResolution.None).ToList();
        RenderDtoConstruction(mapped, prop => $"ctx.{prop.PropertyName}");
    }

    /// <summary>
    ///     Emits the DTO construction. A DTO without a public parameterless ctor (a positional record) is
    ///     built through its primary constructor — matched constructor parameters become positional
    ///     arguments, and any remaining settable properties an object initializer; everything else keeps
    ///     the object-initializer form. <paramref name="valueFor"/> yields each property's value expression.
    /// </summary>
    private void RenderDtoConstruction(List<PropertyMappingModel> props, Func<PropertyMappingModel, string> valueFor)
    {
        var ctorParams = _model.DtoConstructorParameters;
        if (ctorParams.Length == 0)
        {
            // Object-initializer form (records without positional ctor use the parens-less syntax).
            AppendLine(_model.IsRecord ? $"return new {_model.TypeName}" : $"return new {_model.TypeName}()");
            Block(() =>
            {
                foreach (var prop in props)
                    AppendLine($"{prop.PropertyName} = {valueFor(prop)},");
            });
            AppendLine(";");
            return;
        }

        string ArgFor(ConstructorParameterModel cp)
        {
            var match = props.FirstOrDefault(p =>
                string.Equals(p.PropertyName, cp.MatchingPropertyName, StringComparison.OrdinalIgnoreCase));
            return match is not null ? valueFor(match) : "default";
        }

        var ctorArgs = string.Join(", ", ctorParams.Select(ArgFor));

        var remaining = props
            .Where(p => !ctorParams.Any(cp =>
                string.Equals(cp.MatchingPropertyName, p.PropertyName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (remaining.Count == 0)
        {
            AppendLine($"return new {_model.TypeName}({ctorArgs});");
            return;
        }

        AppendLine($"return new {_model.TypeName}({ctorArgs})");
        Block(() =>
        {
            foreach (var prop in remaining)
                AppendLine($"{prop.PropertyName} = {valueFor(prop)},");
        });
        AppendLine(";");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // FromEntityBodyOnly — scalar properties only (no collections/nested/dictionaries)
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderFromEntityBodyOnly()
    {
        AppendLine();
        XmlSummary(
            $"Creates a new {_model.TypeName} from only the scalar properties of the {_model.SourceTypeName} entity. " +
            "Collections, nested DTOs, and dictionaries are excluded.");
        XmlParam("entity", "The source entity to map from.");
        XmlReturns($"A new {_model.TypeName} instance with only scalar properties mapped.");

        var parameters = new List<MethodParameter>
        {
            new("global::" + _model.SourceTypeFullName, "entity")
        };

        Method("FromEntityBodyOnly", RenderFromEntityBodyOnlyBody, _model.TypeName, parameters,
            modifiers: new MethodModifiers { IsStatic = true });
    }

    private void RenderFromEntityBodyOnlyBody()
    {
        if (!_model.SourceTypeIsValueType)
        {
            AppendLine("global::Pragmatic.Ensure.Ensure.ThrowIfNull(entity);");
            AppendLine();
        }

        var bodyProperties = GetBodyOnlyProperties().ToList();
        RenderDtoConstruction(bodyProperties, GenerateMappingExpression);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Partial Methods
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderPartialMethods()
    {
        Comment("Partial methods for customization");
        AppendLine(
            $"static partial void BeforeMapping(global::{_model.SourceTypeFullName} source, ref {_model.TypeName}? result);");
        AppendLine(
            $"static partial void CustomizeMapping(global::{_model.SourceTypeFullName} source, ref {_model.TypeName}MappingContext ctx);");
        AppendLine();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Context Struct
    // ═══════════════════════════════════════════════════════════════════════════

    private void RenderContextStruct()
    {
        Comment("Mutable context for property customization");
        // Not using readonly because fields need to be mutable for customization
        Struct($"{_model.TypeName}MappingContext", () =>
        {
            foreach (var prop in _model.Properties.Where(p => !p.IsIgnored && p.Resolution != MappingResolution.None))
            {
                // Don't add extra ? if the type already ends with ?
                var propType = prop.PropertyType;
                if (prop.IsNullable && !propType.EndsWith("?"))
                    propType += "?";
                AppendLine($"public {propType} {prop.PropertyName};");
            }
        });
    }
}

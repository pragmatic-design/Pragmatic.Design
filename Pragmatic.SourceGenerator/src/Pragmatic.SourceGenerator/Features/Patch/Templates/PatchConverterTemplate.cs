using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Patch.Models;

namespace Pragmatic.SourceGenerator.Features.Patch.Templates;

/// <summary>
///     Generates a System.Text.Json converter that tracks which JSON keys were sent.
///     Present keys → Optional.Of(value); absent keys → Optional.Undefined.
/// </summary>
internal sealed class PatchConverterTemplate : CSharpTemplate
{
    private readonly PatchModel _model;

    public PatchConverterTemplate(PatchModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Patch";
    protected override string? SourceInfo => $"{_model.TypeName}JsonConverter for {_model.EntityName}";
    protected override string? TriggerInfo => $"[GeneratePatch<{_model.EntityName}>] on {_model.TypeName}";

    protected override bool Validate() => _model.IsValid;

    private string ConverterName => $"{_model.TypeName}JsonConverter";

    public override Artifact RenderOutput() => new(
        VirtualFolderHints.ForType(_model.TypeName, "JsonConverter", _model.Namespace),
        ToSourceText());

    public override void RenderFile()
    {
        AddUsings("System", "System.Collections.Generic", "System.Text.Json", "System.Text.Json.Serialization", "Pragmatic.Patch");

        AppendNamespace(_model.Namespace);
        AppendLine();

        // Apply [JsonConverter] attribute on the partial record
        AppendLine($"[JsonConverter(typeof({ConverterName}))]");
        Record(_model.TypeName, () => { },
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });

        // Generate the converter class
        Class(ConverterName, RenderConverterBody,
            baseType: $"JsonConverter<{_model.TypeName}>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderConverterBody()
    {
        RenderReadMethod();
        AppendLine();
        RenderWriteMethod();
        AppendLine();
        RenderHelperMethods();
    }

    private void RenderReadMethod()
    {
        AppendLine($"public override {_model.TypeName} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        Block(RenderReadBody);
        AppendLine();
    }

    private void RenderReadBody()
    {
        foreach (var prop in _model.Properties)
            AppendLine($"var {ToCamelCase(prop.Name)} = Optional<{prop.TypeFullName}>.Undefined;");

        AppendLine();
        If("reader.TokenType != JsonTokenType.StartObject", () =>
        {
            AppendLine("throw new JsonException(\"Expected StartObject token.\");");
        });
        AppendLine();

        AppendLine("while (reader.Read())");
        Block(() =>
        {
            If("reader.TokenType == JsonTokenType.EndObject", () => { Break(); });
            AppendLine();
            If("reader.TokenType != JsonTokenType.PropertyName", () =>
            {
                AppendLine("throw new JsonException(\"Expected PropertyName token.\");");
            });
            AppendLine();

            AppendLine("var propertyName = reader.GetString();");
            AppendLine("reader.Read();");
            AppendLine();

            Switch("propertyName", () =>
            {
                foreach (var prop in _model.Properties)
                {
                    Case($"\"{prop.Name}\"", () =>
                    {
                        RenderReadOptionalInline(prop);
                        Break();
                    });

                    var camelCase = ToCamelCase(prop.Name);
                    if (camelCase != prop.Name)
                    {
                        Case($"\"{camelCase}\"", () =>
                        {
                            RenderReadOptionalInline(prop);
                            Break();
                        });
                    }
                }

                Default(() =>
                {
                    AppendLine("reader.Skip();");
                    Break();
                });
            });
        });

        AppendLine();

        AppendLine($"return new {_model.TypeName}");
        AppendLine("{");
        IncreaseIndent();
        for (var i = 0; i < _model.Properties.Length; i++)
        {
            var prop = _model.Properties[i];
            var comma = i < _model.Properties.Length - 1 ? "," : "";
            AppendLine($"{prop.Name} = {ToCamelCase(prop.Name)}{comma}");
        }
        DecreaseIndent();
        AppendLine("};");
    }

    private void RenderReadOptionalInline(PatchPropertyModel prop)
    {
        var varName = ToCamelCase(prop.Name);

        // Non-nullable value types: JSON null means skip (keep undefined), only read actual values
        if (prop is { IsValueType: true, IsNullable: false })
        {
            If("reader.TokenType == JsonTokenType.Null", () =>
            {
                Comment("JSON null for non-nullable value type — treat as undefined");
            });
            Else(() =>
            {
                AppendLine($"{varName} = Optional<{prop.TypeFullName}>.Of(JsonSerializer.Deserialize(ref reader, Info<{prop.TypeFullName}>(options)));");
            });
        }
        else if (!prop.IsNullable)
        {
            // A non-nullable reference column cannot be cleared, and saying so here is the whole
            // point: accepting the null wrote it through ApplyTo and the refusal came back from the
            // database as a constraint violation — a caller told their body was wrong by the storage
            // engine, naming a column rather than the field they sent. Reading it as "undefined"
            // instead would be worse: the caller asked for something and nothing happened.
            If("reader.TokenType == JsonTokenType.Null", () =>
            {
                AppendLine("throw new global::System.Text.Json.JsonException(");
                IncreaseIndent();
                // propertyName, not a literal: it is the name as the caller sent it, which is what
                // they can look for in their own body. The wire name the writer uses is computed from
                // the serializer options and is not in scope on the read path.
                AppendLine("\"'\" + propertyName + \"' cannot be cleared: \" +");
                AppendLine($"\"{_model.EntityName}.{prop.Name} is not nullable.\");");
                DecreaseIndent();
            });
            Else(() =>
            {
                AppendLine($"{varName} = Optional<{prop.TypeFullName}>.Of(JsonSerializer.Deserialize(ref reader, Info<{prop.TypeFullName}>(options)));");
            });
        }
        else
        {
            If("reader.TokenType == JsonTokenType.Null", () =>
            {
                AppendLine($"{varName} = Optional<{prop.TypeFullName}>.Null;");
            });
            Else(() =>
            {
                AppendLine($"{varName} = Optional<{prop.TypeFullName}>.Of(JsonSerializer.Deserialize(ref reader, Info<{prop.TypeFullName}>(options)));");
            });
        }
    }

    private void RenderWriteMethod()
    {
        RenderTypeInfoHelper();
        AppendLine();
        AppendLine($"public override void Write(Utf8JsonWriter writer, {_model.TypeName} value, JsonSerializerOptions options)");
        Block(RenderWriteBody);
        AppendLine();
    }

    private void RenderWriteBody()
    {
        AppendLine("writer.WriteStartObject();");
        AppendLine();

        foreach (var prop in _model.Properties)
        {
            If($"value.{prop.Name}.HasValue", () =>
            {
                AppendLine($"var name{prop.Name} = ResolvePropertyName(\"{prop.Name}\", options);");

                // Value types that aren't nullable can never be null — skip the null check
                if (prop is { IsValueType: true, IsNullable: false })
                {
                    AppendLine($"writer.WritePropertyName(name{prop.Name});");
                    AppendLine($"JsonSerializer.Serialize(writer, value.{prop.Name}.Value, Info<{prop.TypeFullName}>(options));");
                }
                else
                {
                    If($"value.{prop.Name}.Value is null", () =>
                    {
                        AppendLine($"writer.WriteNull(name{prop.Name});");
                    });
                    Else(() =>
                    {
                        AppendLine($"writer.WritePropertyName(name{prop.Name});");
                        AppendLine($"JsonSerializer.Serialize(writer, value.{prop.Name}.Value, Info<{prop.TypeFullName}>(options));");
                    });
                }
            });
        }

        AppendLine();
        AppendLine("writer.WriteEndObject();");
    }

    private void RenderHelperMethods()
    {
        Method("ResolvePropertyName", () =>
        {
            If("options.PropertyNamingPolicy is not null", () =>
            {
                Return("options.PropertyNamingPolicy.ConvertName(name)");
            });
            Return("name");
        }, "string", new List<MethodParameter>
        {
            new("string", "name"),
            new("JsonSerializerOptions", "options")
        }, AccessModifier.Private, new MethodModifiers { IsStatic = true });
    }

    private static string ToCamelCase(string name) => TemplateHelpers.ToCamelCase(name);

    /// <summary>
    ///     Emits the typed-metadata lookup the read and write paths use.
    /// </summary>
    /// <remarks>
    ///     Written into the converter rather than taken from <c>Pragmatic.Abstractions</c>: a project
    ///     that uses <c>[Patch]</c> does not necessarily reference it, and a generated file may only
    ///     name what its own consumer already has. The untyped <c>Serialize</c>/<c>Deserialize</c>
    ///     overloads it replaces are <c>RequiresUnreferencedCode</c> and fail under AOT.
    /// </remarks>
    private void RenderTypeInfoHelper()
    {
        AppendLine("/// <summary>The metadata for a property's type, from these options.</summary>");
        AppendLine("private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> Info<T>(JsonSerializerOptions options)");
        AppendLine("    => options.GetTypeInfo(typeof(T)) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>");
        AppendLine("       ?? throw new global::System.InvalidOperationException(");
        AppendLine("           $\"No JSON metadata for '{typeof(T)}'. Cover it in a JsonSerializerContext registered with the host options.\");");
    }
}

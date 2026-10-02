using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     The producer of <c>MarkSet</c>: a JSON converter for a <c>[Patch&lt;T&gt;]</c> that marks every
///     property the body names, absent ones excluded.
/// </summary>
/// <remarks>
///     <para>
///         <c>ApplyPatch</c> has had two branches since it was written — one that honours
///         <c>_setProperties</c>, and a fallback that writes whatever is not null — and nothing on the
///         HTTP path ever called <c>MarkSet</c>. A patch read from a body always took the fallback,
///         where <c>{"x": null}</c> and a body without <c>x</c> are the same thing: the one distinction
///         a PATCH exists to make.
///     </para>
///     <para>
///         Same mechanism as the <c>[GeneratePatch]</c> converter: <c>[JsonConverter]</c> on the
///         partial type, so whatever options read the body honour it, and typed metadata
///         (<c>Info&lt;T&gt;</c>) so the property values never go through the reflection-based
///         overloads. The type is the author's — <c>init</c> setters, property initializers — so what
///         the body does not mention takes its value from a fresh instance: the default the author
///         wrote is the default, not <c>default(T)</c>.
///     </para>
///     <para>
///         Property names are matched as declared and in camelCase, like the <c>[GeneratePatch]</c>
///         converter does. Writing mirrors <c>ApplyPatch</c>: the marked properties when something was
///         marked, the non-null ones otherwise, so a round trip keeps the same reading.
///     </para>
/// </remarks>
internal sealed class PatchJsonConverterTemplate : CSharpTemplate
{
    private const string ConverterName = "PatchJsonConverter";

    private readonly MutationMetadataModel _model;

    public PatchJsonConverterTemplate(MutationMetadataModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Patch] on {_model.TypeName}";

    protected override bool Validate() => _model.IsValid;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForType(_model.TypeName, ConverterName, _model.Namespace), ToSourceText());

    /// <summary>The properties the body can set: an accessor to write through, and not static.</summary>
    private IEnumerable<MutationPropertyModel> Settable => _model.Properties.Where(p => p.HasSetter);

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        AppendLine($"[global::System.Text.Json.Serialization.JsonConverter(typeof({_model.TypeName}.{ConverterName}))]");
        Class(_model.TypeName, RenderConverter,
            accessModifier: TemplateHelpers.ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderConverter()
    {
        XmlSummary($"Reads a {_model.TypeName} from JSON, marking every property the body names.");
        Class(ConverterName, RenderConverterBody,
            baseType: $"global::System.Text.Json.Serialization.JsonConverter<{_model.TypeName}>",
            accessModifier: AccessModifier.Internal,
            modifiers: new ClassModifiers { Sealed = true });
    }

    private void RenderConverterBody()
    {
        RenderRead();
        RenderWrite();
        RenderTypeInfoHelper();
        AppendLine();
        RenderPropertyNameHelper();
    }

    private void RenderRead()
    {
        AppendLine($"public override {_model.TypeName} Read(ref global::System.Text.Json.Utf8JsonReader reader, "
            + "global::System.Type typeToConvert, global::System.Text.Json.JsonSerializerOptions options)");
        Block(RenderReadBody);
        AppendLine();
    }

    private void RenderReadBody()
    {
        If("reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject",
            () => AppendLine("throw new global::System.Text.Json.JsonException(\"Expected StartObject token.\");"));
        AppendLine();

        // The author's initializers are the values of what the body does not mention. A required
        // member has to be named to construct at all, and has no initializer to read.
        Comment("Property initializers are the values of what the body does not mention.");
        var required = Settable.Where(p => p.IsRequired).ToList();
        if (required.Count == 0)
        {
            AppendLine($"var __defaults = new {_model.TypeName}();");
        }
        else
        {
            AppendLine($"var __defaults = new {_model.TypeName}");
            AppendLine("{");
            IncreaseIndent();
            foreach (var prop in required)
                AppendLine($"{prop.PropertyName} = default!,");
            DecreaseIndent();
            AppendLine("};");
        }

        foreach (var prop in Settable)
        {
            AppendLine($"var {Local(prop)} = __defaults.{prop.PropertyName};");
            AppendLine($"var {Local(prop)}Set = false;");
        }

        AppendLine();
        AppendLine("while (reader.Read())");
        Block(() =>
        {
            If("reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject", Break);
            AppendLine();
            If("reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName",
                () => AppendLine("throw new global::System.Text.Json.JsonException(\"Expected PropertyName token.\");"));
            AppendLine();
            AppendLine("var propertyName = reader.GetString();");
            AppendLine("reader.Read();");
            AppendLine();

            Switch("propertyName", () =>
            {
                foreach (var prop in Settable)
                {
                    Case($"\"{prop.PropertyName}\"", () => RenderReadProperty(prop));

                    var camelCase = TemplateHelpers.ToCamelCase(prop.PropertyName);
                    if (camelCase != prop.PropertyName)
                        Case($"\"{camelCase}\"", () => RenderReadProperty(prop));
                }

                Default(() =>
                {
                    AppendLine("reader.Skip();");
                    Break();
                });
            });
        });

        AppendLine();
        AppendLine($"var __result = new {_model.TypeName}");
        AppendLine("{");
        IncreaseIndent();
        foreach (var prop in Settable)
            AppendLine($"{prop.PropertyName} = {Local(prop)},");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();

        foreach (var prop in Settable)
            AppendLine($"if ({Local(prop)}Set) __result.MarkSet(nameof({prop.PropertyName}));");

        AppendLine();
        AppendLine("return __result;");
    }

    /// <remarks>
    ///     A JSON null is read by the property's own metadata: null for what can hold it, and a
    ///     <c>JsonException</c> — a 400 — for a value type that cannot. The <c>!</c> is for the
    ///     compiler only: <c>Deserialize</c> returns <c>T?</c>, and the local is typed by the author's
    ///     declaration.
    /// </remarks>
    private void RenderReadProperty(MutationPropertyModel prop)
    {
        AppendLine($"{Local(prop)} = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, "
            + $"Info<{prop.PropertyFullTypeName}>(options))!;");
        AppendLine($"{Local(prop)}Set = true;");
        Break();
    }

    private void RenderWrite()
    {
        AppendLine("public override void Write(global::System.Text.Json.Utf8JsonWriter writer, "
            + $"{_model.TypeName} value, global::System.Text.Json.JsonSerializerOptions options)");
        Block(() =>
        {
            AppendLine("writer.WriteStartObject();");
            AppendLine();
            Comment("The reading ApplyPatch has: the marked properties, or the non-null ones when nothing was marked.");

            foreach (var prop in Settable)
            {
                var marked = $"value.SetProperties.Contains(nameof({prop.PropertyName}))";
                var condition = prop is { IsValueType: true, IsNullable: false }
                    ? $"value.SetProperties.Count == 0 || {marked}"
                    : $"value.SetProperties.Count == 0 ? value.{prop.PropertyName} is not null : {marked}";

                If(condition, () =>
                {
                    AppendLine($"writer.WritePropertyName(ResolvePropertyName(\"{prop.PropertyName}\", options));");
                    AppendLine("global::System.Text.Json.JsonSerializer.Serialize(writer, "
                        + $"value.{prop.PropertyName}, Info<{prop.PropertyFullTypeName}>(options));");
                });
            }

            AppendLine();
            AppendLine("writer.WriteEndObject();");
        });
        AppendLine();
    }

    /// <remarks>
    ///     Written into the converter rather than taken from <c>Pragmatic.Abstractions</c>, like the
    ///     <c>[GeneratePatch]</c> converter does: a generated file may only name what its consumer
    ///     already references, and the untyped overloads it replaces are <c>RequiresUnreferencedCode</c>.
    /// </remarks>
    private void RenderTypeInfoHelper()
    {
        XmlSummary("The metadata for a property's type, from these options.");
        AppendLine("private static global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> Info<T>("
            + "global::System.Text.Json.JsonSerializerOptions options)");
        AppendLine("    => options.GetTypeInfo(typeof(T)) as global::System.Text.Json.Serialization.Metadata.JsonTypeInfo<T>");
        AppendLine("       ?? throw new global::System.InvalidOperationException(");
        AppendLine("           $\"No JSON metadata for '{typeof(T)}'. Cover it in a JsonSerializerContext registered with the host options.\");");
    }

    private void RenderPropertyNameHelper()
    {
        Method("ResolvePropertyName", () =>
        {
            If("options.PropertyNamingPolicy is not null", () => Return("options.PropertyNamingPolicy.ConvertName(name)"));
            Return("name");
        }, "string", [new MethodParameter("string", "name"), new MethodParameter("global::System.Text.Json.JsonSerializerOptions", "options")],
            AccessModifier.Private, new MethodModifiers { IsStatic = true });
    }

    private static string Local(MutationPropertyModel prop) => "__" + TemplateHelpers.ToCamelCase(prop.PropertyName);
}

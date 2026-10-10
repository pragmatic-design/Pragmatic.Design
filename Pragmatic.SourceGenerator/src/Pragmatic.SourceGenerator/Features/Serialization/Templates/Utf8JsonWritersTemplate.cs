using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>
///     Emits the assembly's generated UTF-8 JSON writers, one method per planned type and mask, and a delegate for
///     each entry point: <c>_Infra.Serialization.Utf8JsonWriters.g.cs</c> for logged types,
///     <c>_Infra.Serialization.Utf8ResponseWriters.g.cs</c> for endpoint responses.
/// </summary>
/// <remarks>
///     <para>
///         A writer writes a value straight into a <c>Utf8JsonWriter</c>: no serializer, no metadata lookup, no
///         intermediate copy. What it writes is planned from the type (<c>JsonWriterPlanner</c> for a logged type,
///         <c>JsonResponseWriterPlanner</c> for a response); the <see cref="JsonWriterProfile" /> decides the
///         details the type does not, so each writer reproduces the output of the path it replaces.
///     </para>
///     <para>
///         The delegate exists so a caller that takes an <c>Action&lt;Utf8JsonWriter, T&gt;</c> gets one
///         allocated once, when the class is first touched, rather than one per call.
///     </para>
/// </remarks>
internal sealed partial class Utf8JsonWritersTemplate : CSharpTemplate
{
    /// <summary>The class every writer of a logged type lives in.</summary>
    public const string ClassName = "GeneratedUtf8JsonWriters";

    /// <summary>The class every writer of an endpoint response lives in.</summary>
    public const string ResponseClassName = "GeneratedUtf8ResponseWriters";

    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";
    private const string EncodedText = "global::System.Text.Json.JsonEncodedText";
    private const string Options = "global::System.Text.Json.JsonSerializerOptions";

    private readonly string _namespace;
    private readonly JsonWriterProfile _profile;
    private readonly IReadOnlyList<JsonWriterMethodModel> _methods;
    private readonly Dictionary<string, string> _names = new(System.StringComparer.Ordinal);
    private readonly Dictionary<string, (string Method, JsonWriterValueModel Model)> _enums = new(System.StringComparer.Ordinal);
    private int _locals;

    /// <param name="namespace">The assembly's generated namespace.</param>
    /// <param name="methods">Every planned method; duplicates by name are written once.</param>
    /// <param name="profile">Whose output the writers reproduce.</param>
    public Utf8JsonWritersTemplate(
        string @namespace, IEnumerable<JsonWriterMethodModel> methods, JsonWriterProfile profile = JsonWriterProfile.Log)
    {
        _namespace = @namespace;
        _profile = profile;
        _methods = methods
            .GroupBy(m => m.Name, System.StringComparer.Ordinal)
            // The copy that carries a shape, if one does: a type one endpoint answers with and another nests is
            // planned twice, and only the first plan made it an entry point.
            .Select(g => (g.FirstOrDefault(m => m.Shape is not null) ?? g.First()) with { IsEntryPoint = g.Any(m => m.IsEntryPoint) })
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The expression a caller uses for the delegate of an entry-point method of a logged type.</summary>
    public static string DelegateFor(string @namespace, string method)
        => $"global::{@namespace}.{ClassName}.{method}Delegate";

    /// <summary>The expression a caller uses for the delegate of an entry-point method of a response.</summary>
    public static string ResponseDelegateFor(string @namespace, string method)
        => $"global::{@namespace}.{ResponseClassName}.{method}Delegate";

    /// <summary>The expression a caller uses for the shape of an entry-point method of a response.</summary>
    public static string ResponseShapeFor(string @namespace, string method)
        => $"global::{@namespace}.{ResponseClassName}.{method}Shape";

    /// <summary>
    ///     What the writer writes, as a <c>GeneratedJsonShape</c> the response asks the host's converters about
    ///     before it uses the writer.
    /// </summary>
    private void RenderShape(string method, JsonWriterShapeModel shape)
    {
        var types = string.Join(", ", shape.Types.Select(t => $"typeof({t})"));
        var enums = string.Join(", ", shape.Enums.Select(e => $"typeof({e.EnumType})"));
        var converters = string.Join(", ", shape.Enums.Select(e => e.FastEnumConverter is { } c ? $"typeof({c})" : "null"));

        AppendLine($"internal static readonly global::Pragmatic.Serialization.GeneratedJsonShape {method}Shape = new(");
        AppendLine($"    [{types}],");
        AppendLine($"    [{enums}],");
        AppendLine($"    [{converters}],");
        AppendLine($"    needsInfrastructureExclusion: {(shape.NeedsInfrastructureExclusion ? "true" : "false")});");
    }

    private string WriterClass => _profile == JsonWriterProfile.Response ? ResponseClassName : ClassName;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Serialization";
    protected override string? TriggerInfo => $"{_methods.Count} UTF-8 JSON writer method(s)";

    protected override bool Validate() => _methods.Count > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly(
                "Serialization", _profile == JsonWriterProfile.Response ? "Utf8ResponseWriters" : "Utf8JsonWriters"),
            ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();
        XmlSummary(_profile == JsonWriterProfile.Response
            ? "UTF-8 JSON writers for the responses this assembly's endpoints answer with."
            : "UTF-8 JSON writers for the types this assembly writes without a serializer.");
        AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        Block(() =>
        {
            foreach (var method in _methods.Where(m => m.IsEntryPoint))
                AppendLine($"internal static readonly global::System.Action<{Writer}, {method.TypeExpr}{OptionsTypeArgument}> {method.Name}Delegate = {method.Name};");

            foreach (var method in _methods.Where(m => m is { IsEntryPoint: true, Shape: not null }))
                RenderShape(method.Name, method.Shape!);

            foreach (var method in _methods)
            {
                AppendLine();
                RenderMethod(method);
            }

            // After the bodies, which is where the enums and the names they use are met.
            foreach (var enumMethod in _enums.Values.OrderBy(e => e.Method, System.StringComparer.Ordinal).ToList())
            {
                AppendLine();
                RenderEnumMethod(enumMethod.Method, enumMethod.Model);
            }

            RenderNameFields();
        }, $"internal static class {WriterClass}");
    }

    /// <summary>
    ///     A response writer takes the options the response is answered with, for the values only the serializer can
    ///     write (a member typed <c>object</c>) and the nested writers that pass them on; a log writer takes none.
    /// </summary>
    private string OptionsParameter => _profile == JsonWriterProfile.Response ? $", {Options} options" : "";

    private string OptionsArgument => _profile == JsonWriterProfile.Response ? ", options" : "";

    private string OptionsTypeArgument => _profile == JsonWriterProfile.Response ? $", {Options}" : "";

    private void RenderMethod(JsonWriterMethodModel method)
    {
        AppendLine($"internal static void {method.Name}({Writer} writer, {method.TypeExpr} value{OptionsParameter})");
        Block(() =>
        {
            if (method.Root is { } root)
            {
                if (WritesRuns && IsEncoderFree(root))
                    RenderRun(() => RenderRunValue(root, "value"));
                else
                    RenderValue(root, "value");
                return;
            }

            if (WritesRuns && IsEncoderFree(method.Name))
            {
                RenderRun(() => AppendLine($"{RunMethodName(method.Name)}(ref run, value);"));
                return;
            }

            AppendLine("writer.WriteStartObject();");
            // `@` in each member access, so a member named after a keyword still reads as a member.
            RenderMembers(method);
            AppendLine("writer.WriteEndObject();");
        });

        if (WritesRuns && method.Root is null && IsEncoderFree(method.Name))
        {
            AppendLine();
            RenderRunMethod(method);
        }
    }

    private void RenderMember(JsonWriterMemberModel member, string expression)
    {
        var name = PropertyName(member.JsonName);

        switch (member.Skip)
        {
            case JsonWriterSkip.WhenNull when member.Value.Kind != JsonWriterValueKind.Mask:
                var local = "v" + _locals++;
                // `is { } v` unwraps a Nullable<T> to T and narrows a reference to non-null alike.
                AppendLine($"if ({expression} is {{ }} {local})");
                Block(() => RenderNamed(member.Value, name, local, present: true));
                return;

            case JsonWriterSkip.WhenDefault:
                AppendLine($"if (!global::System.Collections.Generic.EqualityComparer<{member.TypeExpr}>.Default.Equals({expression}, default!))");
                Block(() => RenderNamed(member.Value, name, expression, present: false));
                return;

            default:
                RenderNamed(member.Value, name, expression, present: false);
                return;
        }
    }

    /// <summary>A member's name and its value.</summary>
    /// <remarks>
    ///     A leaf the writer has a named overload for is written in one call (<c>WriteNumber(name, value)</c>), as
    ///     System.Text.Json's own serialization handler writes it: one state check and one buffer request instead
    ///     of two. Measured on <c>citm_catalog.json</c>, mostly small objects of numbers, where the two calls were
    ///     what kept the writer behind the handler. The bytes are the same either way.
    /// </remarks>
    private void RenderNamed(JsonWriterValueModel value, string name, string expression, bool present)
    {
        if (_profile == JsonWriterProfile.Response && Combined(value, name, expression, present) is { } combined)
        {
            AppendLine(combined);
            return;
        }

        AppendLine($"writer.WritePropertyName({name});");
        if (present)
            RenderPresent(value, expression);
        else
            RenderValue(value, expression);
    }

    /// <summary>The one call that writes a leaf with its name, or null when there is none for it.</summary>
    private static string? Combined(JsonWriterValueModel value, string name, string expression, bool present)
    {
        // A value that may be null and is not known present needs the null branch RenderValue writes; a string
        // is the exception, because WriteString writes a null string as null itself.
        if (!present && value.CanBeNull && value.Kind != JsonWriterValueKind.String)
            return null;

        return value.Kind switch
        {
            JsonWriterValueKind.Number => $"writer.WriteNumber({name}, ({value.Cast}){expression});",
            JsonWriterValueKind.String or JsonWriterValueKind.StringValue
                or JsonWriterValueKind.DateTime or JsonWriterValueKind.DateTimeOffset => $"writer.WriteString({name}, {expression});",
            JsonWriterValueKind.Boolean => $"writer.WriteBoolean({name}, {expression});",
            _ => null,
        };
    }

    /// <summary>The argument a property name is written with: a <c>u8</c> literal, or a pre-encoded field.</summary>
    private string PropertyName(string jsonName)
    {
        if (_profile == JsonWriterProfile.Log)
            return $"\"{StringHelper.CSharpLiteral(jsonName)}\"u8";

        if (!_names.TryGetValue(jsonName, out var field))
            _names[jsonName] = field = "__Name" + _names.Count;

        return field;
    }

    private void RenderNameFields()
    {
        if (_names.Count == 0)
            return;

        // Encoded once, with the encoder the response is written with: what the serializer does with the options'.
        AppendLine();
        foreach (var name in _names)
            AppendLine($"private static readonly {EncodedText} {name.Value} = global::Pragmatic.Serialization.GeneratedJsonDefaults.Encode(\"{StringHelper.CSharpLiteral(name.Key)}\");");

        RenderRawNameFields();
    }
}

using System.Linq;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Templates;

/// <summary>The state struct: the list view, the UTF-8 message and the JSON properties.</summary>
/// <remarks>
///     ⚠️ A masked parameter is written as the mask in all four places — the list view, the message, the
///     JSON properties and <c>ToString()</c> — and its value never enters the struct: the method does not
///     pass it to the constructor. Nothing downstream can read what the state does not hold, a provider
///     that bypasses the interface included.
/// </remarks>
internal abstract partial class LogCallSiteTemplateBase
{
    private const string Format = "global::Pragmatic.Logging.CallSites.Utf8LogFormat";
    private const string Pair = "global::System.Collections.Generic.KeyValuePair<string, object?>";
    private const string Mask = "global::Pragmatic.Serialization.RedactionMask.Value";

    private void RenderState(LogCallSiteModel callSite, string stateName)
    {
        var properties = callSite.Parameters.Where(p => p.IsProperty).ToList();

        AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        AppendLine($"private readonly struct {stateName}");
        AppendLine($"    : global::System.Collections.Generic.IReadOnlyList<{Pair}>,");
        AppendLine("      global::Pragmatic.Logging.CallSites.IUtf8LogState,");
        AppendLine($"      global::Pragmatic.Logging.CallSites.IUtf8LogStateWriter<{stateName}>");
        Block(() =>
        {
            // The type initializer runs before the call site's first read of Format, so the writer is in
            // place before any provider looks for it. One box, here, instead of one per call.
            AppendLine($"static {stateName}() => global::Pragmatic.Logging.CallSites.Utf8LogStateWriters<{stateName}>.Writer = default({stateName});");
            AppendLine();
            AppendLine($"public static readonly global::System.Func<{stateName}, global::System.Exception?, string> Format =");
            AppendLine("    static (state, _) => state.ToString();");
            AppendLine();

            for (var i = 0; i < properties.Count; i++)
                AppendLine($"private static readonly global::System.Text.Json.JsonEncodedText __Name{i} = global::System.Text.Json.JsonEncodedText.Encode({Literal(properties[i].Key)});");
            if (properties.Count > 0)
                AppendLine();

            var held = properties.Where(p => !p.IsMasked).ToList();
            foreach (var property in held)
                AppendLine($"private readonly {property.Type} {Field(property)};");
            if (held.Count > 0)
                AppendLine();

            var arguments = string.Join(", ", held.Select(p => $"{p.Type} {Identifier(p.Name)}"));
            AppendLine($"public {stateName}({arguments})");
            Block(() =>
            {
                foreach (var property in held)
                    AppendLine($"{Field(property)} = {Identifier(property.Name)};");
            });
            AppendLine();

            AppendLine($"public string Template => {Literal(callSite.Template)};");
            AppendLine();
            AppendLine($"public bool IsSelfContained => {(callSite.IsSelfContained ? "true" : "false")};");
            AppendLine();

            RenderList(callSite, properties);
            AppendLine();
            RenderMessage(callSite, properties);
            AppendLine();
            RenderProperties(callSite, properties, stateName);
            AppendLine();

            AppendLine($"public bool TryFormatMessage(in {stateName} state, global::System.Span<byte> destination, out int bytesWritten)");
            AppendLine("    => state.TryFormatMessage(destination, out bytesWritten);");
            AppendLine();
            AppendLine($"public void WriteProperties(in {stateName} state, global::System.Text.Json.Utf8JsonWriter writer)");
            AppendLine("    => state.WriteProperties(writer);");
            AppendLine();

            AppendLine($"public override string ToString() => {Format}.Render(this);");
        });
    }

    private void RenderList(LogCallSiteModel callSite, System.Collections.Generic.List<LogParameterModel> properties)
    {
        AppendLine($"public int PropertyCount => {properties.Count};");
        AppendLine();
        AppendLine($"public int Count => {properties.Count + 1};");
        AppendLine();
        AppendLine($"public {Pair} this[int index] => index switch");
        AppendLine("{");
        IncreaseIndent();
        for (var i = 0; i < properties.Count; i++)
        {
            var value = properties[i].IsMasked ? Mask : Field(properties[i]);
            AppendLine($"{i} => new {Pair}({Literal(properties[i].Key)}, {value}),");
        }

        AppendLine($"{properties.Count} => new {Pair}(\"{{OriginalFormat}}\", {Literal(callSite.Template)}),");
        AppendLine("_ => throw new global::System.ArgumentOutOfRangeException(nameof(index)),");
        DecreaseIndent();
        AppendLine("};");
        AppendLine();
        AppendLine($"public global::System.Collections.Generic.IEnumerator<{Pair}> GetEnumerator()");
        Block(() =>
        {
            AppendLine("for (var i = 0; i < Count; i++)");
            AppendLine("    yield return this[i];");
        });
        AppendLine();
        AppendLine("global::System.Collections.IEnumerator global::System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();");
    }

    private void RenderMessage(LogCallSiteModel callSite, System.Collections.Generic.List<LogParameterModel> properties)
    {
        AppendLine("public bool TryFormatMessage(global::System.Span<byte> destination, out int bytesWritten)");
        Block(() =>
        {
            AppendLine("var written = 0;");
            if (callSite.Parts.Count > 0)
            {
                AppendLine("if (");
                IncreaseIndent();
                for (var i = 0; i < callSite.Parts.Count; i++)
                {
                    var part = callSite.Parts[i];
                    var append = part.Literal is { } literal
                        ? $"{Format}.TryAppend(destination, ref written, {Utf8Literal(literal)})"
                        : AppendValue(callSite.Parameters[part.ParameterIndex], part.Format);
                    AppendLine((i == 0 ? "!" : "|| !") + append);
                }

                DecreaseIndent();
                AppendLine(")");
                Block(() =>
                {
                    AppendLine("bytesWritten = 0;");
                    AppendLine("return false;");
                });
                AppendLine();
            }

            AppendLine("bytesWritten = written;");
            AppendLine("return true;");
        });
    }

    private static string AppendValue(LogParameterModel parameter, string format)
    {
        if (parameter.IsMasked)
            return $"{Format}.TryAppendMask(destination, ref written)";

        var field = Field(parameter);
        var formatArgument = Literal(format);
        return parameter.Kind switch
        {
            LogValueKind.String or LogValueKind.Boolean => $"{Format}.TryAppend(destination, ref written, {field})",
            LogValueKind.Enum => $"{Format}.TryAppendEnum(destination, ref written, {field}, {formatArgument})",
            LogValueKind.Number or LogValueKind.Formattable =>
                $"{Format}.TryAppendFormatted(destination, ref written, {field}, {formatArgument})",
            _ => $"{Format}.TryAppendObject(destination, ref written, {field})",
        };
    }

    private void RenderProperties(LogCallSiteModel callSite, System.Collections.Generic.List<LogParameterModel> properties, string stateName)
    {
        AppendLine("public void WriteProperties(global::System.Text.Json.Utf8JsonWriter writer)");
        Block(() =>
        {
            if (!callSite.IsSelfContained)
            {
                // The contract: a provider reads the list view when IsSelfContained is false.
                AppendLine("throw new global::System.InvalidOperationException(");
                AppendLine($"    \"{stateName} holds a value it cannot write itself; IsSelfContained is false, read the list view.\");");
                return;
            }

            for (var i = 0; i < properties.Count; i++)
                AppendLine(WriteValue(properties[i], $"__Name{i}") + ";");
        });
    }

    private static string WriteValue(LogParameterModel parameter, string name)
    {
        if (parameter.IsMasked)
            return $"{Format}.WriteMask(writer, {name})";

        var field = Field(parameter);
        return parameter.Kind switch
        {
            LogValueKind.String => $"writer.WriteString({name}, {field})",
            LogValueKind.Boolean when parameter.IsNullableValueType =>
                $"if ({field}.HasValue) writer.WriteBoolean({name}, {field}.Value); else writer.WriteNull({name})",
            LogValueKind.Boolean => $"writer.WriteBoolean({name}, {field})",
            LogValueKind.Number when parameter.IsNullableValueType =>
                $"if ({field}.HasValue) writer.WriteNumber({name}, ({parameter.NumberType}){field}.Value); else writer.WriteNull({name})",
            LogValueKind.Number => $"writer.WriteNumber({name}, ({parameter.NumberType}){field})",
            LogValueKind.Enum => $"{Format}.WriteEnum(writer, {name}, {field})",
            _ => $"{Format}.WriteFormatted(writer, {name}, {field}, {Literal(parameter.JsonFormat)})",
        };
    }

    private static string Field(LogParameterModel parameter) => "_" + parameter.Name;
}

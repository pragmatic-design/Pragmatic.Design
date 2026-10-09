using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>How one value is written: a member's, an element's, or a whole response's.</summary>
internal sealed partial class Utf8JsonWritersTemplate
{
    private const string Mask = "global::Pragmatic.Serialization.RedactionMask.Utf8";
    private const string LogHelper = "global::Pragmatic.Logging.CallSites.Utf8LogJson";
    private const string Values = "global::Pragmatic.Serialization.Utf8JsonValues";

    private void RenderValue(JsonWriterValueModel value, string expression)
    {
        if (value.Kind == JsonWriterValueKind.Mask && !value.CanBeNull)
        {
            // The value is not even read: what the type declared must not be logged never reaches the writer.
            AppendLine($"writer.WriteStringValue({Mask});");
            return;
        }

        if (value.CanBeNull && value.Kind != JsonWriterValueKind.String)
        {
            var local = "v" + _locals++;
            AppendLine($"if ({expression} is {{ }} {local})");
            // `is { } v` unwraps a Nullable<T> to T and narrows a reference to non-null alike.
            Block(() => RenderPresent(value, local));
            AppendLine("else");
            Block(() => AppendLine("writer.WriteNullValue();"));
            return;
        }

        RenderPresent(value, expression);
    }

    /// <summary>A value known not to be null.</summary>
    private void RenderPresent(JsonWriterValueModel value, string expression)
    {
        switch (value.Kind)
        {
            case JsonWriterValueKind.String:
            case JsonWriterValueKind.StringValue:
                AppendLine($"writer.WriteStringValue({expression});");
                break;
            case JsonWriterValueKind.Boolean:
                AppendLine($"writer.WriteBooleanValue({expression});");
                break;
            case JsonWriterValueKind.Number:
            case JsonWriterValueKind.Enum:
                AppendLine($"writer.WriteNumberValue(({value.Cast}){expression});");
                break;
            case JsonWriterValueKind.EnumName:
                AppendLine($"{EnumMethod(value)}(writer, {expression});");
                break;
            case JsonWriterValueKind.Char:
                AppendLine($"{Values}.WriteChar(writer, {expression});");
                break;
            case JsonWriterValueKind.TimeSpan:
                AppendLine($"{Values}.WriteTimeSpan(writer, {expression});");
                break;
            case JsonWriterValueKind.DateOnly:
                AppendLine($"{Values}.WriteDateOnly(writer, {expression});");
                break;
            case JsonWriterValueKind.TimeOnly:
                AppendLine($"{Values}.WriteTimeOnly(writer, {expression});");
                break;
            case JsonWriterValueKind.DateTime:
                AppendLine(_profile == JsonWriterProfile.Response
                    ? $"writer.WriteStringValue({expression});"
                    : $"{LogHelper}.WriteDateTime(writer, {expression});");
                break;
            case JsonWriterValueKind.DateTimeOffset:
                AppendLine(_profile == JsonWriterProfile.Response
                    ? $"writer.WriteStringValue({expression});"
                    : $"{LogHelper}.WriteDateTimeOffset(writer, {expression});");
                break;
            case JsonWriterValueKind.Uri:
                AppendLine($"writer.WriteStringValue({expression}.OriginalString);");
                break;
            case JsonWriterValueKind.Base64:
                AppendLine($"writer.WriteBase64StringValue({expression});");
                break;
            case JsonWriterValueKind.Object:
                AppendLine($"{value.Method}(writer, {expression}{OptionsArgument});");
                break;
            case JsonWriterValueKind.Mask:
                AppendLine($"writer.WriteStringValue({Mask});");
                break;
            case JsonWriterValueKind.Untyped:
                AppendLine($"{Values}.WriteUntyped(writer, {expression}, options);");
                break;
            case JsonWriterValueKind.Collection:
                var element = "e" + _locals++;
                AppendLine("writer.WriteStartArray();");
                AppendLine($"foreach (var {element} in {expression})");
                Block(() => RenderValue(value.Element!, element));
                AppendLine("writer.WriteEndArray();");
                break;
            case JsonWriterValueKind.Dictionary:
                var entry = "kv" + _locals++;
                AppendLine("writer.WriteStartObject();");
                AppendLine($"foreach (var {entry} in {expression})");
                Block(() =>
                {
                    AppendLine(value.Cast.Length == 0
                        ? $"writer.WritePropertyName({entry}.Key);"
                        : $"{Values}.WritePropertyName(writer, ({value.Cast}){entry}.Key);");
                    RenderValue(value.Element!, entry + ".Value");
                });
                AppendLine("writer.WriteEndObject();");
                break;
        }
    }

    /// <summary>The method that writes an enum by name, registered the first time the enum is met.</summary>
    private string EnumMethod(JsonWriterValueModel value)
    {
        if (!_enums.TryGetValue(value.EnumType, out var known))
        {
            var token = value.EnumType.Replace("global::", "").Replace('.', '_').Replace('+', '_');
            _enums[value.EnumType] = known = ("WriteEnum_" + token, value);
        }

        return known.Method;
    }

    /// <summary>
    ///     A declared member by its pre-encoded name, anything else as its number: what <c>JsonStringEnumConverter</c>
    ///     writes, with integer values allowed, as the host registers it.
    /// </summary>
    private void RenderEnumMethod(string method, JsonWriterValueModel value)
    {
        AppendLine($"private static void {method}({Writer} writer, {value.EnumType} value)");
        Block(() =>
        {
            AppendLine("switch (value)");
            Block(() =>
            {
                foreach (var member in value.EnumNames)
                {
                    AppendLine($"case {value.EnumType}.@{member.Member}:");
                    AppendLine($"    writer.WriteStringValue({PropertyName(member.WireName)});");
                    AppendLine("    return;");
                }

                AppendLine("default:");
                AppendLine($"    writer.WriteNumberValue(({value.Cast})value);");
                AppendLine("    return;");
            });
        });
    }
}

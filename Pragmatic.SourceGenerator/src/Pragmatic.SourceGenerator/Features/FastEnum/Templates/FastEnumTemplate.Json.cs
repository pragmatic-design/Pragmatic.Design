using System.Globalization;
using Pragmatic.SourceGenerator.Features.FastEnum.Models;

namespace Pragmatic.SourceGenerator.Features.FastEnum.Templates;

/// <summary>
///     JSON-facing half of the FastEnum template: the generated <c>{Type}JsonConverter</c> and the
///     zero-reflection flag composition it relies on.
///     <para>
///         The converter is registered by the host in place of <c>JsonStringEnumConverter</c>, so it
///         must be observationally identical to it — same wire format, same accept/reject decision.
///         <c>FastEnumJsonConverterEquivalenceTests</c> pins that case by case against the real
///         framework converter; change anything here and re-run it.
///     </para>
/// </summary>
internal sealed partial class FastEnumTemplate
{
    /// <summary>Enum underlying types that cannot hold a negative value.</summary>
    private bool IsUnsignedUnderlying =>
        _model.UnderlyingType is "byte" or "ushort" or "uint" or "ulong";

    /// <summary>Widest integer the flag bit arithmetic can run in without losing a value.</summary>
    private string AccumulatorType => IsUnsignedUnderlying ? "ulong" : "long";

    /// <summary>
    ///     The <see cref="System.Text.Json.Utf8JsonWriter"/> WriteNumberValue overload to target:
    ///     the narrow types have none of their own and widen to <c>int</c>.
    /// </summary>
    private string NumberWriteType => _model.UnderlyingType switch
    {
        "uint" => "uint",
        "long" => "long",
        "ulong" => "ulong",
        _ => "int"
    };

    /// <summary>
    ///     The <see cref="System.Text.Json.Utf8JsonReader"/> accessor for the underlying type. Using the
    ///     exact type (not a wider one) is what makes an out-of-range number fail like the framework
    ///     converter does — e.g. 300 on a byte-backed enum.
    /// </summary>
    private string ReaderTryGetMethod => _model.UnderlyingType switch
    {
        "byte" => "TryGetByte",
        "sbyte" => "TryGetSByte",
        "short" => "TryGetInt16",
        "ushort" => "TryGetUInt16",
        "uint" => "TryGetUInt32",
        "long" => "TryGetInt64",
        "ulong" => "TryGetUInt64",
        _ => "TryGetInt32"
    };

    private const string InvariantCulture = "global::System.Globalization.CultureInfo.InvariantCulture";
    private const string IntegerStyles = "global::System.Globalization.NumberStyles.Integer";

    /// <summary>Renders the numeric rendering of an undeclared value — what Enum.ToString() falls back to.</summary>
    private string NumericFormatExpression => $"(({_model.UnderlyingType})value).ToString({InvariantCulture})";

    /// <summary>Whether a member is declared with the value zero (it names the "no flags set" state).</summary>
    private bool HasZeroMember => _model.Members.Any(m => ParseMemberValue(m.Value) == 0m);

    /// <summary>
    ///     Non-zero members, largest first. The BCL formats flags by matching from the largest value
    ///     down and prepending each hit, so a composite member (<c>All = Read | Write</c>) wins over its
    ///     parts. Matching that order is what keeps the output identical.
    /// </summary>
    private List<FastEnumMemberModel> FlagMembersDescending =>
        _model.Members
            .Where(m => ParseMemberValue(m.Value) != 0m)
            .OrderByDescending(m => ParseMemberValue(m.Value))
            .ToList();

    /// <summary>
    ///     Parses a member's constant value for ordering only. <c>decimal</c> spans both
    ///     <c>long.MinValue</c> and <c>ulong.MaxValue</c>, which no single integer type does.
    /// </summary>
    private static decimal ParseMemberValue(string value) =>
        decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    /// <summary>
    ///     Formats a flag combination as the BCL would, without reflection: names when every bit is
    ///     covered by declared members, the number otherwise.
    /// </summary>
    private void RenderFormatFlags()
    {
        var accumulator = AccumulatorType;
        var flags = FlagMembersDescending;
        var count = flags.Count;

        XmlSummary("Formats a flag combination as the comma-separated declared member names, or the numeric value when some bits are not covered by any member.");
        AppendLine($"private static string FormatFlags({_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"var remaining = ({accumulator})value;");
        AppendLine("if (remaining == 0)");
        AppendLine($"    return {NumericFormatExpression};");
        AppendLine();
        AppendLine($"var matched = new string[{count}];");
        AppendLine($"var index = {count};");

        foreach (var member in flags)
        {
            var flag = $"({accumulator}){_model.TypeName}.{member.Name}";
            AppendLine($"if ((remaining & {flag}) == {flag})");
            AppendLine("{");
            IncreaseIndent();
            AppendLine($"matched[--index] = nameof({_model.TypeName}.{member.Name});");
            AppendLine($"remaining &= ~{flag};");
            DecreaseIndent();
            AppendLine("}");
        }

        AppendLine();
        Comment("Leftover bits (or nothing matched) — the BCL prints the raw number in that case.");
        AppendLine($"if (remaining != 0 || index == {count})");
        AppendLine($"    return {NumericFormatExpression};");
        AppendLine();
        AppendLine($"return string.Join(\", \", matched, index, {count} - index);");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Renders the "every bit is declared" test. <c>IsDefined</c> answers a different question —
    ///     whether the value IS a member — and would reject <c>Read | Write</c>.
    /// </summary>
    private void RenderIsFlagsDefined()
    {
        var accumulator = AccumulatorType;

        XmlSummary("Checks whether every bit of a flag combination is covered by declared members (unlike IsDefined, which only accepts a single declared member).");
        AppendLine($"public static bool IsFlagsDefined({_model.TypeName} value)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"var remaining = ({accumulator})value;");
        AppendLine("if (remaining == 0)");
        AppendLine($"    return {(HasZeroMember ? "true" : "false")};");

        foreach (var member in FlagMembersDescending)
            AppendLine($"remaining &= ~({accumulator}){_model.TypeName}.{member.Name};");

        AppendLine("return remaining == 0;");
        DecreaseIndent();
        AppendLine("}");
    }

    /// <summary>
    ///     Renders the drop-in replacement for <c>JsonStringEnumConverter</c>.
    /// </summary>
    private void RenderJsonConverter()
    {
        AddUsing("System.Text.Json");
        AddUsing("System.Text.Json.Serialization");

        var type = _model.TypeName;
        var definedCheck = _model.IsFlags ? "IsFlagsDefined" : "IsDefined";

        XmlSummary($"AOT-safe JSON converter for <see cref=\"{type}\"/>. Serializes as the string name, zero reflection. Observationally identical to JsonStringEnumConverter.");
        AppendLine($"{_model.Accessibility} sealed class {type}JsonConverter : JsonConverter<{type}>");
        AppendLine("{");
        IncreaseIndent();

        Comment("Same wording as the framework converter, so a failure reads the same to callers.");
        AppendLine($"private const string ConversionError = \"The JSON value could not be converted to {type}.\";");
        AppendLine();

        // Read — a number is accepted because allowIntegerValues:true is the framework default.
        AppendLine($"public override {type} Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("if (reader.TokenType == JsonTokenType.Number)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"if (reader.{ReaderTryGetMethod}(out var numeric))");
        AppendLine($"    return ({type})numeric;");
        AppendLine("throw new JsonException(ConversionError);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        Comment("Anything that is neither a number nor a string (null, true, {}, []) is a hard failure.");
        AppendLine("if (reader.TokenType != JsonTokenType.String)");
        AppendLine("    throw new JsonException(ConversionError);");
        AppendLine();
        AppendLine("return ParseName(reader.GetString());");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        // Write — an undeclared value goes out as a number, never as a quoted number.
        AppendLine($"public override void Write(Utf8JsonWriter writer, {type} value, JsonSerializerOptions options)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"if ({type}Extensions.{definedCheck}(value))");
        AppendLine("    writer.WriteStringValue(value.ToStringFast());");
        AppendLine("else");
        AppendLine($"    writer.WriteNumberValue(({NumberWriteType})value);");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        // Property-name overrides — without them every Dictionary<TEnum, V> throws NotSupportedException.
        Comment($"Dictionary keys: without these overrides every Dictionary<{type}, T> throws at runtime.");
        AppendLine($"public override {type} ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("return ParseName(reader.GetString());");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        AppendLine($"public override void WriteAsPropertyName(Utf8JsonWriter writer, {type} value, JsonSerializerOptions options)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine("writer.WritePropertyName(value.ToStringFast());");
        DecreaseIndent();
        AppendLine("}");
        AppendLine();

        // Shared string parsing: a name (or flag combination), else a numeric string.
        AppendLine($"private static {type} ParseName(string? name)");
        AppendLine("{");
        IncreaseIndent();
        AppendLine($"if ({type}Extensions.TryParse(name, ignoreCase: true, out var value))");
        AppendLine("    return value;");
        Comment("The framework converter also accepts the number as a string (\"1\").");
        AppendLine($"if (name is not null && {_model.UnderlyingType}.TryParse(name, {IntegerStyles}, {InvariantCulture}, out var numeric))");
        AppendLine($"    return ({type})numeric;");
        AppendLine("throw new JsonException(ConversionError);");
        DecreaseIndent();
        AppendLine("}");

        DecreaseIndent();
        AppendLine("}");
    }
}

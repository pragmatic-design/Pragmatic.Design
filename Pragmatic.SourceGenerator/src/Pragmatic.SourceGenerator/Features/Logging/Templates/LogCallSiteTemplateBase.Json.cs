using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Templates;

/// <summary>
///     The state's message and its properties as JSON bytes in one pass (<c>TryFormatMessageAndJson</c>): each value
///     the message renders without a format is formatted once, and its bytes are the property's.
/// </summary>
/// <remarks>
///     <para>
///         Emitted only when every property is a value whose JSON the bytes can be — an integer or a decimal, a
///         string, a boolean, or masked — and every name is one no encoder escapes. Otherwise the method says so
///         (<c>NeedsWriter</c>), and the provider writes the properties through <c>WriteProperties</c> as before.
///     </para>
///     <para>
///         A string's bytes are the JSON only when the encoder would escape nothing in them; when it would, the
///         generated code reports <c>NeedsWriter</c> for that call, so the escaping is always the writer's own.
///     </para>
/// </remarks>
internal abstract partial class LogCallSiteTemplateBase
{
    private const string Values = "global::Pragmatic.Logging.CallSites.Utf8LogJsonValues";
    private const string JsonStatus = "global::Pragmatic.Logging.CallSites.Utf8LogJsonStatus";

    // The numbers whose JSON is the digits their general format writes; float and double are not among them.
    private static readonly HashSet<string> JsonNumbers = ["int", "uint", "long", "ulong", "decimal"];

    private void RenderJson(LogCallSiteModel callSite, List<LogParameterModel> properties, string stateName)
    {
        var writes = WritesJson(callSite, properties);
        AppendLine($"public bool WritesJsonProperties => {(writes ? "true" : "false")};");
        AppendLine();

        AppendLine($"public {JsonStatus} TryFormatMessageAndJson(global::System.Span<byte> message, global::System.Span<byte> json, out int messageWritten, out int jsonWritten)");
        Block(() =>
        {
            AppendLine("messageWritten = 0;");
            AppendLine("jsonWritten = 0;");
            if (!writes)
            {
                AppendLine($"return {JsonStatus}.NeedsWriter;");
                return;
            }

            var shared = SharedValues(callSite, properties);
            RenderJsonMessage(callSite, shared);
            RenderJsonProperties(callSite, properties, shared);
        });
        AppendLine();

        AppendLine($"public {JsonStatus} TryFormatMessageAndJson(in {stateName} state, global::System.Span<byte> message, global::System.Span<byte> json, out int messageWritten, out int jsonWritten)");
        AppendLine("    => state.TryFormatMessageAndJson(message, json, out messageWritten, out jsonWritten);");
    }

    // The message as TryFormatMessage renders it, the start and length of each shared value marked.
    private void RenderJsonMessage(LogCallSiteModel callSite, Dictionary<int, int> shared)
    {
        foreach (var slot in shared.Values.OrderBy(s => s))
            AppendLine($"int __at{slot} = 0, __length{slot} = 0;");

        AppendLine("var written = 0;");
        if (callSite.Parts.Count > 0)
        {
            var marked = new HashSet<int>();
            var appends = new List<string>();
            foreach (var part in callSite.Parts)
            {
                if (part.Literal is { } literal)
                {
                    appends.Add($"{Format}.TryAppend(message, ref written, {Utf8Literal(literal)})");
                    continue;
                }

                var parameter = callSite.Parameters[part.ParameterIndex];
                var append = AppendValue(parameter, part.Format, "message");
                if (shared.TryGetValue(part.ParameterIndex, out var slot) && marked.Add(part.ParameterIndex))
                {
                    appends.Add($"{Values}.Mark(written, out __at{slot})");
                    appends.Add(append);
                    appends.Add($"{Values}.Mark(written - __at{slot}, out __length{slot})");
                }
                else
                {
                    appends.Add(append);
                }
            }

            RenderChain(appends, () => AppendLine($"return {JsonStatus}.BufferTooSmall;"));
        }

        AppendLine("messageWritten = written;");
    }

    private void RenderJsonProperties(LogCallSiteModel callSite, List<LogParameterModel> properties, Dictionary<int, int> shared)
    {
        AppendLine("var needsWriter = false;");
        AppendLine("var j = 0;");

        var appends = new List<string>();
        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            appends.Add($"{Format}.TryAppend(json, ref j, {Utf8Literal((i == 0 ? "\"" : ",\"") + property.Key + "\":")})");
            appends.Add(JsonValue(property, shared.TryGetValue(IndexOf(callSite, property), out var slot) ? slot : -1));
        }

        RenderChain(appends, () =>
        {
            AppendLine("messageWritten = 0;");
            AppendLine($"return needsWriter ? {JsonStatus}.NeedsWriter : {JsonStatus}.BufferTooSmall;");
        });
        AppendLine("jsonWritten = j;");
        AppendLine($"return {JsonStatus}.Written;");
    }

    private void RenderChain(List<string> appends, System.Action onFailure)
    {
        if (appends.Count == 0)
            return;

        AppendLine("if (");
        IncreaseIndent();
        for (var i = 0; i < appends.Count; i++)
            AppendLine((i == 0 ? "!" : "|| !") + appends[i]);
        DecreaseIndent();
        AppendLine(")");
        Block(onFailure);
        AppendLine();
    }

    // The property's value: from the message's bytes when it shares them (slot ≥ 0), formatted otherwise.
    private static string JsonValue(LogParameterModel property, int slot)
    {
        if (property.IsMasked)
            return $"{Values}.TryAppendJsonMask(json, ref j)";

        var field = Field(property);
        var rendered = $"message.Slice(__at{slot}, __length{slot})";
        return property.Kind switch
        {
            LogValueKind.Number when slot >= 0 => $"{Format}.TryAppend(json, ref j, {rendered})",
            LogValueKind.Number when property.IsNullableValueType =>
                $"{Values}.TryAppendJsonNumber(json, ref j, ({property.NumberType}?){field})",
            LogValueKind.Number => $"{Values}.TryAppendJsonNumber(json, ref j, ({property.NumberType}){field})",
            LogValueKind.String when slot >= 0 =>
                $"{Values}.TryAppendJsonString(json, ref j, {field}, {rendered}, ref needsWriter)",
            LogValueKind.String => $"{Values}.TryAppendJsonString(json, ref j, {field}, ref needsWriter)",
            _ => $"{Values}.TryAppendJsonBoolean(json, ref j, {field})",
        };
    }

    private static int IndexOf(LogCallSiteModel callSite, LogParameterModel parameter)
    {
        for (var i = 0; i < callSite.Parameters.Count; i++)
        {
            if (ReferenceEquals(callSite.Parameters[i], parameter))
                return i;
        }

        return -1;
    }

    /// <summary>
    ///     The parameters whose message bytes are also their JSON, by parameter index, each with a slot number: a
    ///     non-nullable number or a string the message renders without a format, a number of the types whose JSON
    ///     is their general format.
    /// </summary>
    private static Dictionary<int, int> SharedValues(LogCallSiteModel callSite, List<LogParameterModel> properties)
    {
        var shared = new Dictionary<int, int>();
        foreach (var part in callSite.Parts)
        {
            if (part.Literal is not null || shared.ContainsKey(part.ParameterIndex))
                continue;

            var parameter = callSite.Parameters[part.ParameterIndex];
            if (!parameter.IsProperty || parameter.IsMasked)
                continue;

            var sharesBytes = parameter.Kind switch
            {
                LogValueKind.Number => !parameter.IsNullableValueType && part.Format.Length == 0,
                LogValueKind.String => true,
                _ => false,
            };
            if (sharesBytes && properties.Contains(parameter))
                shared[part.ParameterIndex] = shared.Count;
        }

        return shared;
    }

    private static bool WritesJson(LogCallSiteModel callSite, List<LogParameterModel> properties)
        => callSite.IsSelfContained
           && properties.Count > 0
           && properties.All(p => p.IsMasked || p.Kind switch
           {
               LogValueKind.Number => JsonNumbers.Contains(p.NumberType),
               LogValueKind.String or LogValueKind.Boolean => true,
               _ => false,
           })
           && properties.All(p => p.Key.Length > 0 && p.Key.All(IsPlainNameCharacter));

    // Letters, digits and the underscore: characters no JSON encoder escapes in a property name.
    private static bool IsPlainNameCharacter(char c)
        => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_';
}

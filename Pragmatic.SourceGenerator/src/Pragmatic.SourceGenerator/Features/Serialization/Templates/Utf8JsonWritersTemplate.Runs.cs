using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>
///     The parts of a response a run can write with the writer's very bytes, formatted into a <c>Utf8JsonRun</c> and
///     handed to the writer in one <c>WriteRawValue</c>.
/// </summary>
/// <remarks>
///     <para>
///         Every writer call pays a state check, a buffer request and a separator decision. On a document made mostly
///         of small objects of numbers that is most of the time spent: measured by subtraction on
///         <c>citm_catalog.json</c> (<c>Pragmatic.Endpoints/BENCHMARK-RESULTS.md</c>), where writing these parts as
///         runs took the writer from level with System.Text.Json's own serialization handler to well ahead of it.
///     </para>
///     <para>
///         What fits a run: a name already encoded, a number, a boolean, a Guid, a date, an enum by number, a string,
///         and objects, arrays and integer-keyed dictionaries made only of those. A string is escaped by the run as the
///         writer escapes it under the response encoder (#152), which is the only encoder a generated response is
///         written with: <c>GeneratedJsonDefaults.AllowGeneratedWriters</c> sends anything else to the serializer. An
///         enum by name is pre-encoded text chosen at run time, and stays with the writer. A floating-point number that
///         is not finite, or a string longer than the writer takes, is refused by the run as the writer refuses it.
///     </para>
///     <para>
///         ⚠️ A run of members starts with a name, written where the writer expects one: the writer must skip
///         validation. Every caller writes with <c>GeneratedJsonDefaults.ResponseWriterOptions</c>, which does —
///         <c>GeneratedJsonResponse</c>, and the tests that compare a writer with the serializer.
///     </para>
/// </remarks>
internal sealed partial class Utf8JsonWritersTemplate
{
    private const string Run = "global::Pragmatic.Serialization.Utf8JsonRun";

    private Dictionary<string, JsonWriterMethodModel>? _byName;
    private readonly Dictionary<string, bool> _free = new(System.StringComparer.Ordinal);
    private readonly HashSet<string> _rawNames = new(System.StringComparer.Ordinal);

    private bool WritesRuns => _profile == JsonWriterProfile.Response;

    /// <summary>Whether a run writes <paramref name="value" /> with the writer's very bytes.</summary>
    private bool FitsARun(JsonWriterValueModel value) => value.Kind switch
    {
        JsonWriterValueKind.String => true,
        JsonWriterValueKind.Number => value.Cast is "int" or "uint" or "long" or "ulong" or "decimal" or "double" or "float",
        JsonWriterValueKind.Enum => value.Cast is "int" or "uint" or "long" or "ulong",
        JsonWriterValueKind.Boolean or JsonWriterValueKind.StringValue
            or JsonWriterValueKind.DateTime or JsonWriterValueKind.DateTimeOffset => true,
        JsonWriterValueKind.Collection => FitsARun(value.Element!),
        JsonWriterValueKind.Dictionary => value.Cast.Length > 0 && FitsARun(value.Element!),
        JsonWriterValueKind.Object => FitsARun(value.Method),
        _ => false,
    };

    /// <summary>Whether every member the method writes fits a run: the whole object is one run.</summary>
    private bool FitsARun(string method)
    {
        if (_free.TryGetValue(method, out var known))
            return known;

        _byName ??= _methods.ToDictionary(m => m.Name, System.StringComparer.Ordinal);

        // Settled as not free while it is being decided: a type that reaches itself is refused by the planner, and
        // this keeps the recursion finite if one ever is not.
        _free[method] = false;
        var free = _byName.TryGetValue(method, out var model)
                   && model.Root is null
                   && model.Members.All(m => FitsARun(m.Value));
        _free[method] = free;
        return free;
    }

    /// <summary>
    ///     Whether a group of consecutive members that fit a run is worth a run: one scalar is one writer call either
    ///     way, a container or two members is not.
    /// </summary>
    private static bool WorthARun(IReadOnlyList<JsonWriterMemberModel> group)
        => group.Count > 1 || group[0].Value.Kind is JsonWriterValueKind.Collection
            or JsonWriterValueKind.Dictionary or JsonWriterValueKind.Object;

    private static string RunMethodName(string method)
        => method.StartsWith("Write_", System.StringComparison.Ordinal) ? "Run_" + method.Substring("Write_".Length) : method + "_Run";

    /// <summary>The members of an object, consecutive ones that fit a run written as runs.</summary>
    private void RenderMembers(JsonWriterMethodModel method)
    {
        var members = method.Members;
        var i = 0;
        while (i < members.Count)
        {
            if (!WritesRuns || !FitsARun(members[i].Value))
            {
                RenderMember(members[i], "value.@" + members[i].ClrName);
                i++;
                continue;
            }

            var group = new List<JsonWriterMemberModel>();
            while (i < members.Count && FitsARun(members[i].Value))
                group.Add(members[i++]);

            if (!WorthARun(group))
            {
                foreach (var member in group)
                    RenderMember(member, "value.@" + member.ClrName);
                continue;
            }

            Block(() => RenderRun(() =>
            {
                foreach (var member in group)
                    RenderRunMember(member, "value.@" + member.ClrName);
            }));
        }
    }

    /// <summary>A run around <paramref name="body" />, handed to the writer when it holds anything.</summary>
    private void RenderRun(System.Action body)
    {
        AppendLine($"var run = {Run}.Start();");
        body();
        AppendLine("if (!run.IsEmpty)");
        AppendLine("    writer.WriteRawValue(run.Written, skipInputValidation: true);");
        AppendLine("run.Dispose();");
    }

    /// <summary>A method of an object that fits a run that writes into a run, for the runs that contain one.</summary>
    private void RenderRunMethod(JsonWriterMethodModel method)
    {
        AppendLine($"internal static void {RunMethodName(method.Name)}(ref {Run} run, {method.TypeExpr} value)");
        Block(() =>
        {
            AppendLine("run.StartObject();");
            foreach (var member in method.Members)
                RenderRunMember(member, "value.@" + member.ClrName);
            AppendLine("run.EndObject();");
        });
    }

    private void RenderRunMember(JsonWriterMemberModel member, string expression)
    {
        var name = RawName(member.JsonName);

        switch (member.Skip)
        {
            case JsonWriterSkip.WhenNull:
                var local = "v" + _locals++;
                AppendLine($"if ({expression} is {{ }} {local})");
                Block(() =>
                {
                    AppendLine($"run.PropertyName({name});");
                    RenderRunPresent(member.Value, local);
                });
                return;

            case JsonWriterSkip.WhenDefault:
                AppendLine($"if (!global::System.Collections.Generic.EqualityComparer<{member.TypeExpr}>.Default.Equals({expression}, default!))");
                Block(() =>
                {
                    AppendLine($"run.PropertyName({name});");
                    RenderRunValue(member.Value, expression);
                });
                return;

            default:
                AppendLine($"run.PropertyName({name});");
                RenderRunValue(member.Value, expression);
                return;
        }
    }

    private void RenderRunValue(JsonWriterValueModel value, string expression)
    {
        if (!value.CanBeNull)
        {
            RenderRunPresent(value, expression);
            return;
        }

        var local = "v" + _locals++;
        AppendLine($"if ({expression} is {{ }} {local})");
        Block(() => RenderRunPresent(value, local));
        AppendLine("else");
        Block(() => AppendLine("run.NullValue();"));
    }

    private void RenderRunPresent(JsonWriterValueModel value, string expression)
    {
        switch (value.Kind)
        {
            case JsonWriterValueKind.Number:
            case JsonWriterValueKind.Enum:
                AppendLine($"run.NumberValue(({value.Cast}){expression});");
                break;
            case JsonWriterValueKind.Boolean:
                AppendLine($"run.BooleanValue({expression});");
                break;
            case JsonWriterValueKind.String:
            case JsonWriterValueKind.StringValue:
            case JsonWriterValueKind.DateTime:
            case JsonWriterValueKind.DateTimeOffset:
                AppendLine($"run.StringValue({expression});");
                break;
            case JsonWriterValueKind.Object:
                AppendLine($"{RunMethodName(value.Method)}(ref run, {expression});");
                break;
            case JsonWriterValueKind.Collection:
                var element = "e" + _locals++;
                AppendLine("run.StartArray();");
                AppendLine($"foreach (var {element} in {expression})");
                Block(() => RenderRunValue(value.Element!, element));
                AppendLine("run.EndArray();");
                break;
            case JsonWriterValueKind.Dictionary:
                var entry = "kv" + _locals++;
                AppendLine("run.StartObject();");
                AppendLine($"foreach (var {entry} in {expression})");
                Block(() =>
                {
                    AppendLine($"run.PropertyName(({value.Cast}){entry}.Key);");
                    RenderRunValue(value.Element!, entry + ".Value");
                });
                AppendLine("run.EndObject();");
                break;
        }
    }

    /// <summary>The field holding <c>"name":</c> for a run, made from the name's encoded field.</summary>
    private string RawName(string jsonName)
    {
        var field = "__Raw" + PropertyName(jsonName).Substring("__".Length);
        _rawNames.Add(jsonName);
        return field;
    }

    private void RenderRawNameFields()
    {
        // After the encoded names, which static initialization runs first, in the order the fields are declared.
        foreach (var jsonName in _rawNames.OrderBy(n => _names[n], System.StringComparer.Ordinal))
        {
            var field = _names[jsonName];
            AppendLine($"private static readonly byte[] __Raw{field.Substring("__".Length)} = {Run}.Name({field});");
        }
    }
}

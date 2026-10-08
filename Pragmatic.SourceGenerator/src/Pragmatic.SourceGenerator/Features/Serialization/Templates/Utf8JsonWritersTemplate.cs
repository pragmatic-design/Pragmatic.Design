using System.Collections.Generic;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Serialization.Models;

namespace Pragmatic.SourceGenerator.Features.Serialization.Templates;

/// <summary>
///     Emits <c>_Infra.Serialization.Utf8JsonWriters.g.cs</c>: the assembly's generated UTF-8 JSON writers, one
///     method per planned type and mask, and a delegate for each entry point.
/// </summary>
/// <remarks>
///     <para>
///         A writer writes a value straight into a <c>Utf8JsonWriter</c>: no serializer, no metadata lookup, no
///         intermediate copy to mask. What it writes is planned by <c>JsonWriterPlanner</c> from the type's JSON
///         shape, so it is what the generated context would serialize — with the mask already in place where
///         the plan says.
///     </para>
///     <para>
///         The delegate exists so a caller that takes an <c>Action&lt;Utf8JsonWriter, T&gt;</c> gets one
///         allocated once, when the class is first touched, rather than one per call.
///     </para>
/// </remarks>
internal sealed class Utf8JsonWritersTemplate : CSharpTemplate
{
    /// <summary>The class every writer lives in.</summary>
    public const string ClassName = "GeneratedUtf8JsonWriters";

    private const string Mask = "global::Pragmatic.Serialization.RedactionMask.Utf8";
    private const string Helper = "global::Pragmatic.Logging.CallSites.Utf8LogJson";
    private const string Writer = "global::System.Text.Json.Utf8JsonWriter";

    private readonly string _namespace;
    private readonly IReadOnlyList<JsonWriterMethodModel> _methods;
    private int _locals;

    /// <param name="namespace">The assembly's generated namespace.</param>
    /// <param name="methods">Every planned method; duplicates by name are written once.</param>
    public Utf8JsonWritersTemplate(string @namespace, IEnumerable<JsonWriterMethodModel> methods)
    {
        _namespace = @namespace;
        _methods = methods
            .GroupBy(m => m.Name, System.StringComparer.Ordinal)
            .Select(g => g.First() with { IsEntryPoint = g.Any(m => m.IsEntryPoint) })
            .OrderBy(m => m.Name, System.StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The expression a caller uses for the delegate of an entry-point method.</summary>
    public static string DelegateFor(string @namespace, string method)
        => $"global::{@namespace}.{ClassName}.{method}Delegate";

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Serialization";
    protected override string? TriggerInfo => $"{_methods.Count} UTF-8 JSON writer method(s)";

    protected override bool Validate() => _methods.Count > 0;

    public override Artifact RenderOutput()
        => new(VirtualFolderHints.ForAssembly("Serialization", "Utf8JsonWriters"), ToSourceText());

    public override void RenderFile()
    {
        AppendNamespace(_namespace);
        AppendLine();
        XmlSummary("UTF-8 JSON writers for the types this assembly writes without a serializer.");
        AppendLine("[global::System.ComponentModel.EditorBrowsable(global::System.ComponentModel.EditorBrowsableState.Never)]");
        Block(() =>
        {
            foreach (var method in _methods.Where(m => m.IsEntryPoint))
                AppendLine($"internal static readonly global::System.Action<{Writer}, {method.TypeExpr}> {method.Name}Delegate = {method.Name};");

            foreach (var method in _methods)
            {
                AppendLine();
                RenderMethod(method);
            }
        }, $"internal static class {ClassName}");
    }

    private void RenderMethod(JsonWriterMethodModel method)
    {
        AppendLine($"internal static void {method.Name}({Writer} writer, {method.TypeExpr} value)");
        Block(() =>
        {
            AppendLine("writer.WriteStartObject();");
            foreach (var member in method.Members)
            {
                AppendLine($"writer.WritePropertyName(\"{StringHelper.CSharpLiteral(member.JsonName)}\"u8);");
                RenderValue(member.Value, "value." + member.ClrName);
            }

            AppendLine("writer.WriteEndObject();");
        });
    }

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
            case JsonWriterValueKind.Char:
                AppendLine($"{Helper}.WriteChar(writer, {expression});");
                break;
            case JsonWriterValueKind.TimeSpan:
                AppendLine($"{Helper}.WriteTimeSpan(writer, {expression});");
                break;
            case JsonWriterValueKind.DateTime:
                AppendLine($"{Helper}.WriteDateTime(writer, {expression});");
                break;
            case JsonWriterValueKind.DateTimeOffset:
                AppendLine($"{Helper}.WriteDateTimeOffset(writer, {expression});");
                break;
            case JsonWriterValueKind.Object:
                AppendLine($"{value.Method}(writer, {expression});");
                break;
            case JsonWriterValueKind.Mask:
                AppendLine($"writer.WriteStringValue({Mask});");
                break;
            case JsonWriterValueKind.Collection:
                var element = "e" + _locals++;
                AppendLine("writer.WriteStartArray();");
                AppendLine($"foreach (var {element} in {expression})");
                Block(() => RenderValue(value.Element!, element));
                AppendLine("writer.WriteEndArray();");
                break;
        }
    }
}

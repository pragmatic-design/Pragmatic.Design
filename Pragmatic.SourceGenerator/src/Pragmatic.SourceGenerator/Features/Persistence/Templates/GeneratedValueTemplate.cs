using System;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Generates the <c>IDefaultValueGenerator&lt;TEntity, string&gt;</c> that formats an
///     <c>[GeneratedValue]</c> value from its template at entity-creation time. Handles the
///     app-side tokens (date / <c>{RANDOM:N}</c> / <c>{GUID:N}</c>); sequence tokens are excluded upstream.
/// </summary>
internal sealed class GeneratedValueTemplate : CSharpTemplate
{
    private const string Invariant = "global::System.Globalization.CultureInfo.InvariantCulture";

    private readonly GeneratedValueModel _model;

    public GeneratedValueTemplate(GeneratedValueModel model) => _model = model;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.EntityShortName}.{_model.PropertyName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[GeneratedValue] on {_model.EntityShortName}.{_model.PropertyName}";

    public override Artifact RenderOutput()
        => new(
            VirtualFolderHints.ForType(_model.EntityShortName, $"{_model.PropertyName}.ValueGenerator", _model.Namespace),
            ToSourceText());

    public override void RenderFile()
    {
        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        AppendLine(
            $"public sealed class {_model.GeneratorClassName} : "
            + $"global::Pragmatic.Persistence.Lifecycle.IDefaultValueGenerator<global::{_model.EntityFullName}, string>");
        Block(RenderClassBody);
    }

    private void RenderClassBody()
    {
        // {SEQ:N} needs a DB round-trip, so inject the boundary-keyed DbContext and make GenerateAsync
        // truly async. The other tokens are self-contained and stay synchronous.
        if (_model.HasSequence)
            RenderSequenceConstructor();

        var asyncModifier = _model.HasSequence ? "async " : string.Empty;
        AppendLine($"public {asyncModifier}global::System.Threading.Tasks.Task<string> GenerateAsync(");
        IncreaseIndent();
        AppendLine($"global::{_model.EntityFullName} entity,");
        AppendLine("global::Pragmatic.Persistence.Lifecycle.LifecycleContext context,");
        AppendLine("global::System.Threading.CancellationToken ct)");
        DecreaseIndent();
        Block(RenderGenerateBody);

        if (_model.Segments.AsImmutableArray().Any(s => s.Kind == GeneratedValueTokenKind.Random))
        {
            AppendLine();
            RenderRandomHelper();
        }
    }

    private void RenderSequenceConstructor()
    {
        AppendLine("private readonly global::Microsoft.EntityFrameworkCore.DbContext _db;");
        AppendLine();
        var dbParam = string.IsNullOrEmpty(_model.TargetBoundaryFullName)
            ? "global::Microsoft.EntityFrameworkCore.DbContext db"
            : $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({Qualify(_model.TargetBoundaryFullName!)}))] global::Microsoft.EntityFrameworkCore.DbContext db";
        AppendLine($"public {_model.GeneratorClassName}({dbParam})");
        Block(() => AppendLine("_db = db;"));
        AppendLine();
    }

    private static string Qualify(string fullTypeName)
        => fullTypeName.StartsWith("global::", StringComparison.Ordinal) ? fullTypeName : $"global::{fullTypeName}";

    private void RenderGenerateBody()
    {
        AppendLine("var __sb = new global::System.Text.StringBuilder();");

        foreach (var segment in _model.Segments.AsImmutableArray())
        {
            switch (segment.Kind)
            {
                case GeneratedValueTokenKind.Literal:
                    AppendLine($"__sb.Append(\"{Escape(segment.Literal)}\");");
                    break;
                case GeneratedValueTokenKind.Year4:
                    AppendLine($"__sb.Append(context.Now.Year.ToString(\"D4\", {Invariant}));");
                    break;
                case GeneratedValueTokenKind.Year2:
                    AppendLine($"__sb.Append((context.Now.Year % 100).ToString(\"D2\", {Invariant}));");
                    break;
                case GeneratedValueTokenKind.Month:
                    AppendLine($"__sb.Append(context.Now.Month.ToString(\"D2\", {Invariant}));");
                    break;
                case GeneratedValueTokenKind.Day:
                    AppendLine($"__sb.Append(context.Now.Day.ToString(\"D2\", {Invariant}));");
                    break;
                case GeneratedValueTokenKind.Random:
                    AppendLine($"__sb.Append(__Random({segment.Length}));");
                    break;
                case GeneratedValueTokenKind.Guid:
                    // A GUID "N" string is 32 chars; clamp the requested width so Substring never throws.
                    AppendLine($"__sb.Append(global::System.Guid.NewGuid().ToString(\"N\").Substring(0, {Math.Min(segment.Length, 32)}));");
                    break;
                case GeneratedValueTokenKind.Sequence:
                    AppendLine(
                        $"__sb.Append((await global::Pragmatic.Persistence.EFCore.Sequences.SequenceValueProvider.NextAsync(_db, \"{Escape(_model.SequenceName!)}\", ct).ConfigureAwait(false))" +
                        $".ToString(\"D{segment.Length}\", {Invariant}));");
                    break;
            }
        }

        if (_model.HasSequence)
            AppendLine("return __sb.ToString();");
        else
            AppendLine("return global::System.Threading.Tasks.Task.FromResult(__sb.ToString());");
    }

    private void RenderRandomHelper()
    {
        AppendLine("private static string __Random(int count)");
        Block(() =>
        {
            AppendLine("const string __chars = \"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789\";");
            AppendLine("var __buffer = new char[count];");
            AppendLine("for (var __i = 0; __i < count; __i++)");
            IncreaseIndent();
            AppendLine("__buffer[__i] = __chars[global::System.Random.Shared.Next(__chars.Length)];");
            DecreaseIndent();
            AppendLine("return new string(__buffer);");
        });
    }

    private static string Escape(string literal)
        => literal.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

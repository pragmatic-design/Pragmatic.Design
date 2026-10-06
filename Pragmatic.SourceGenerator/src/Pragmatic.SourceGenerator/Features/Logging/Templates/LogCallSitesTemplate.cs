using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Templates;

/// <summary>
///     Writes the bodies of a type's <c>[LoggerMessage]</c> methods and, for each, the
///     <c>readonly struct</c> its state travels in.
/// </summary>
/// <remarks>
///     <para>
///         The body checks <c>IsEnabled</c> (unless the attribute skips it) and calls
///         <c>ILogger.Log&lt;TState&gt;</c> with the state and a cached static formatter. Nothing on that
///         path boxes or allocates: the state is a struct, and <c>Log&lt;TState&gt;</c> is generic over it.
///     </para>
///     <para>
///         Also used, through <see cref="RenderCallSite" />, by the templates whose generated types log: a
///         generator never sees another generator's output, its own included, so a call site declared in
///         generated code has to get its body in the same pass that writes the declaration.
///     </para>
/// </remarks>
internal sealed partial class LogCallSitesTemplate : CSharpTemplate
{
    private readonly IReadOnlyList<LogCallSiteModel> _callSites;

    public LogCallSitesTemplate(IReadOnlyList<LogCallSiteModel> callSites) => _callSites = callSites;

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Logging";

    protected override bool Validate() => _callSites.Count > 0;

    public override Artifact RenderOutput()
    {
        var first = _callSites[0];
        var typeName = string.Join(".", first.Containers.Select(c => HintSafe(c.Name)));
        return new Artifact(VirtualFolderHints.ForType(typeName, "LogCallSites", first.Namespace), ToSourceText());
    }

    public override void RenderFile()
    {
        var first = _callSites[0];
        if (first.Namespace.Length > 0)
            AppendNamespace(first.Namespace);
        AppendLine();

        RenderContainers(first.Containers.AsImmutableArray(), 0, () =>
        {
            var names = new HashSet<string>(System.StringComparer.Ordinal);
            for (var i = 0; i < _callSites.Count; i++)
            {
                if (i > 0)
                    AppendLine();

                RenderCallSite(_callSites[i], StateName(_callSites[i], names));
            }
        });
    }

    private void RenderContainers(System.Collections.Immutable.ImmutableArray<LogContainerModel> containers, int index, System.Action body)
    {
        if (index == containers.Length)
        {
            body();
            return;
        }

        var container = containers[index];
        var modifier = (container.IsStatic ? "static " : "") + "partial " + container.Keyword + " " + container.Name;
        Block(() => RenderContainers(containers, index + 1, body), modifier);
    }

    /// <summary>The method's body and its state, inside whatever type the caller has opened.</summary>
    public void RenderCallSite(LogCallSiteModel callSite, string stateName)
    {
        RenderMethod(callSite, stateName);
        AppendLine();
        RenderState(callSite, stateName);
    }

    private void RenderMethod(LogCallSiteModel callSite, string stateName)
    {
        var signature = string.Join(", ", callSite.Parameters.Select(p => $"{p.Type} {Identifier(p.Name)}"));
        // A masked argument is not handed to the state at all: see RenderState.
        var properties = callSite.Parameters.Where(p => p.IsProperty && !p.IsMasked).ToList();
        var exception = callSite.Parameters.FirstOrDefault(p => p.Role == LogParameterRole.Exception);
        var logger = Identifier(callSite.LoggerExpression);
        var level = callSite.LevelExpression.StartsWith("global::", System.StringComparison.Ordinal)
            ? callSite.LevelExpression
            : Identifier(callSite.LevelExpression);

        AppendLine("[global::System.CodeDom.Compiler.GeneratedCode(\"Pragmatic.SourceGenerator\", \"1.0\")]");
        AppendLine($"{callSite.Modifiers} partial void {callSite.MethodName}({signature})");
        Block(() =>
        {
            if (!callSite.SkipEnabledCheck)
            {
                AppendLine($"if (!{logger}.IsEnabled({level}))");
                AppendLine("    return;");
                AppendLine();
            }

            AppendLine($"{logger}.Log(");
            IncreaseIndent();
            AppendLine($"{level},");
            AppendLine($"new global::Microsoft.Extensions.Logging.EventId({callSite.EventId}, {Literal(callSite.EventName)}),");
            AppendLine($"new {stateName}({string.Join(", ", properties.Select(p => Identifier(p.Name)))}),");
            AppendLine($"{(exception is null ? "null" : Identifier(exception.Name))},");
            AppendLine($"{stateName}.Format);");
            DecreaseIndent();
        });
    }

    /// <summary>The state's name: <c>__{Method}LogState</c>, numbered when a method name repeats.</summary>
    public static string StateName(LogCallSiteModel callSite, HashSet<string> taken)
    {
        var name = "__" + callSite.MethodName + "LogState";
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++)
            candidate = name + n;
        return candidate;
    }

    private static string HintSafe(string name)
    {
        var open = name.IndexOf('<');
        return open < 0 ? name : name.Substring(0, open) + "_" + (name.Count(c => c == ',') + 1);
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string Utf8Literal(string text) => Literal(text) + "u8";

    /// <summary>A parameter or member name usable as an identifier, keywords escaped.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}

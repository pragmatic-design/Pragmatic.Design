using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Logging.Models;

namespace Pragmatic.SourceGenerator.Features.Logging.Templates;

/// <summary>
///     Writes a log call site — the method's body and the <c>readonly struct</c> its state travels in —
///     into whatever type the deriving template has opened.
/// </summary>
/// <remarks>
///     <para>
///         The body checks <c>IsEnabled</c> (unless the call site skips it) and calls
///         <c>ILogger.Log&lt;TState&gt;</c> with the state and a cached static formatter. Nothing on that
///         path boxes or allocates: the state is a struct, and <c>Log&lt;TState&gt;</c> is generic over it.
///     </para>
///     <para>
///         A base class rather than a template of its own because the templates whose generated types log
///         derive from it: a generator never sees another generator's output, its own included, so a call
///         site in generated code has to get its body in the same pass that writes the type around it.
///     </para>
/// </remarks>
internal abstract partial class LogCallSiteTemplateBase : CSharpTemplate
{
    /// <summary>The method's body and its state, inside whatever type the caller has opened.</summary>
    protected void RenderCallSite(LogCallSiteModel callSite, string stateName)
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
        AppendLine($"{callSite.Modifiers} {(callSite.IsPartialImplementation ? "partial " : "")}void {callSite.MethodName}({signature})");
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
    protected static string StateName(LogCallSiteModel callSite, HashSet<string> taken)
    {
        var name = "__" + callSite.MethodName + "LogState";
        var candidate = name;
        for (var n = 2; !taken.Add(candidate); n++)
            candidate = name + n;
        return candidate;
    }

    private static string Literal(string text) => SymbolDisplay.FormatLiteral(text, quote: true);

    private static string Utf8Literal(string text) => Literal(text) + "u8";

    /// <summary>A parameter or member name usable as an identifier, keywords escaped.</summary>
    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;
}

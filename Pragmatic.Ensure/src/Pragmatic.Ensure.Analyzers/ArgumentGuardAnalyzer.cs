using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Ensure.Analyzers;

/// <summary>
///     Reports <c>PRAG0100</c> where a guard is written by hand that <c>Ensure</c> already expresses.
/// </summary>
/// <remarks>
///     <para>
///         Three shapes, because those are the three the repository actually contains:
///         <c>x ?? throw new ArgumentNullException(…)</c>, <c>if (x is null) throw new
///         ArgumentNullException(…)</c>, and <c>if (string.IsNullOrEmpty(s)) throw new
///         ArgumentException(…)</c>. Recognising more shapes than exist would be guessing at style
///         nobody writes.
///     </para>
///     <para>
///         <b>Only argument guards.</b> A <c>throw</c> that is not about validating a parameter — a
///         state check, an unreachable branch, a domain rule — is none of Ensure's business, and a
///         analyzer that flagged those would be noise people learn to suppress. The test is the
///         exception type: <c>ArgumentNullException</c>, <c>ArgumentException</c>,
///         <c>ArgumentOutOfRangeException</c>.
///     </para>
///     <para>
///         Never fires inside <c>Pragmatic.Ensure</c> itself, which is where those throws are the
///         implementation rather than a bypass of it.
///     </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ArgumentGuardAnalyzer : DiagnosticAnalyzer
{
    private const string EnsureNamespace = "Pragmatic.Ensure";

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.HandWrittenArgumentGuard);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeThrow, SyntaxKind.ThrowExpression);
        context.RegisterSyntaxNodeAction(AnalyzeThrowStatement, SyntaxKind.ThrowStatement);
    }

    /// <summary><c>x ?? throw new ArgumentNullException(nameof(x))</c>.</summary>
    private static void AnalyzeThrow(SyntaxNodeAnalysisContext context)
    {
        var expression = (ThrowExpressionSyntax)context.Node;

        if (expression.Parent is not BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce)
            return;

        if (!IsArgumentException(context, expression.Expression, out var exceptionName, out var replacement))
            return;

        Report(context, expression.GetLocation(), Describe(coalesce.Left), exceptionName, replacement);
    }

    /// <summary><c>if (x is null) throw new ArgumentNullException(nameof(x));</c> and its siblings.</summary>
    private static void AnalyzeThrowStatement(SyntaxNodeAnalysisContext context)
    {
        var statement = (ThrowStatementSyntax)context.Node;
        if (statement.Expression is null)
            return;

        // The throw must be the whole body of an if, with no else: anything richer is control flow,
        // not a guard, and rewriting it as Ensure would change what the method does.
        var condition = GuardCondition(statement);
        if (condition is null)
            return;

        if (!IsArgumentException(context, statement.Expression, out var exceptionName, out var replacement))
            return;

        Report(context, statement.GetLocation(), Describe(condition), exceptionName, replacement);
    }

    private static ExpressionSyntax? GuardCondition(ThrowStatementSyntax statement)
    {
        var owner = statement.Parent switch
        {
            IfStatementSyntax direct => direct,
            BlockSyntax { Statements.Count: 1, Parent: IfStatementSyntax wrapped } => wrapped,
            _ => null,
        };

        return owner is { Else: null } ? owner.Condition : null;
    }

    private static bool IsArgumentException(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax thrown,
        out string exceptionName,
        out string replacement)
    {
        exceptionName = "";
        replacement = "";

        if (thrown is not ObjectCreationExpressionSyntax creation)
            return false;

        if (context.SemanticModel.GetSymbolInfo(creation.Type, context.CancellationToken).Symbol
            is not INamedTypeSymbol type)
            return false;

        exceptionName = type.Name;
        replacement = type.Name switch
        {
            "ArgumentNullException" => "ThrowIfNull",
            "ArgumentOutOfRangeException" => "ThrowIfOutOfRange",
            "ArgumentException" => "ThrowIfNullOrWhiteSpace",
            _ => "",
        };

        return replacement.Length > 0;
    }

    /// <summary>The guarded thing, for the message — the expression as written.</summary>
    private static string Describe(ExpressionSyntax expression) => expression.ToString();

    private static void Report(
        SyntaxNodeAnalysisContext context,
        Location location,
        string guarded,
        string exceptionName,
        string replacement)
    {
        // Ensure's own throws are the implementation of the rule, not a bypass of it.
        var containing = context.ContainingSymbol?.ContainingNamespace?.ToDisplayString() ?? "";
        if (containing == EnsureNamespace || containing.StartsWith(EnsureNamespace + ".", System.StringComparison.Ordinal))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            DiagnosticDescriptors.HandWrittenArgumentGuard, location, guarded, exceptionName, replacement));
    }
}

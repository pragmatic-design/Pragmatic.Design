// =============================================================================
// Pragmatic.Temporal.Analyzers - DateTimeOffset Comparison Analyzer
// Detects direct relational comparisons on DateTimeOffset values
// =============================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Temporal.Analyzers;

/// <summary>
///     Analyzer that detects direct relational comparisons (&lt;, &gt;, &lt;=, &gt;=)
///     on DateTimeOffset values. These comparisons use the default operator which compares
///     the UtcDateTime internally, but the intent is often ambiguous. Suggests using
///     .UtcDateTime or .ToUniversalTime() explicitly before comparing.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeOffsetComparisonAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<SyntaxKind> RelationalOperators = ImmutableHashSet.Create(
        SyntaxKind.LessThanExpression,
        SyntaxKind.GreaterThanExpression,
        SyntaxKind.LessThanOrEqualExpression,
        SyntaxKind.GreaterThanOrEqualExpression);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.AvoidDateTimeOffsetDirectComparison);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeBinaryExpression,
            SyntaxKind.LessThanExpression,
            SyntaxKind.GreaterThanExpression,
            SyntaxKind.LessThanOrEqualExpression,
            SyntaxKind.GreaterThanOrEqualExpression);
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binaryExpression = (BinaryExpressionSyntax)context.Node;

        // Check if either side is DateTimeOffset
        var leftType = context.SemanticModel.GetTypeInfo(binaryExpression.Left, context.CancellationToken).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binaryExpression.Right, context.CancellationToken).Type;

        if (!IsDateTimeOffset(leftType) && !IsDateTimeOffset(rightType))
            return;

        // Check if either side already uses .UtcDateTime or .ToUniversalTime()
        // If so, the developer is already being explicit — don't warn
        if (IsExplicitUtcAccess(binaryExpression.Left) || IsExplicitUtcAccess(binaryExpression.Right))
            return;

        var operatorToken = binaryExpression.OperatorToken.Text;
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.AvoidDateTimeOffsetDirectComparison,
            binaryExpression.OperatorToken.GetLocation(),
            operatorToken);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsDateTimeOffset(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        return type.Name == "DateTimeOffset" &&
               type.ContainingNamespace?.ToDisplayString() == "System";
    }

    /// <summary>
    ///     Checks if the expression is already accessing .UtcDateTime or calling .ToUniversalTime().
    /// </summary>
    private static bool IsExplicitUtcAccess(ExpressionSyntax expression)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var memberName = memberAccess.Name.Identifier.Text;
            return memberName == "UtcDateTime";
        }

        if (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax invokedMember })
        {
            var methodName = invokedMember.Name.Identifier.Text;
            return methodName == "ToUniversalTime";
        }

        return false;
    }
}

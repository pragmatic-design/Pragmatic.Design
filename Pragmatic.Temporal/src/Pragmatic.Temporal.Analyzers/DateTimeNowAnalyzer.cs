// =============================================================================
// Pragmatic.Temporal.Analyzers - DateTime.Now Analyzer
// Detects direct usage of DateTime.Now and similar properties
// =============================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Temporal.Analyzers;

/// <summary>
///     Analyzer that detects direct usage of DateTime.Now, DateTime.UtcNow,
///     DateTimeOffset.Now, DateTimeOffset.UtcNow, and DateTime.Today.
///     Suggests using IClock or TimeProvider for testable time operations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeNowAnalyzer : DiagnosticAnalyzer
{
    // Properties to detect (member access on DateTime/DateTimeOffset)
    private static readonly ImmutableHashSet<string> TimeProperties = ImmutableHashSet.Create(
        "Now",
        "UtcNow");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(
            DiagnosticDescriptors.AvoidDateTimeNow,
            DiagnosticDescriptors.AvoidDateTimeToday);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;
        var memberName = memberAccess.Name.Identifier.Text;

        // Check if accessing DateTime or DateTimeOffset
        if (memberAccess.Expression is not IdentifierNameSyntax identifier)
            return;

        var typeName = identifier.Identifier.Text;

        // Handle DateTime.Today separately
        if (typeName == "DateTime" && memberName == "Today")
        {
            var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
            if (IsSystemDateTime(symbolInfo.Symbol))
            {
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.AvoidDateTimeToday,
                    memberAccess.GetLocation());
                context.ReportDiagnostic(diagnostic);
            }
            return;
        }

        // Handle Now and UtcNow
        if (!TimeProperties.Contains(memberName))
            return;

        if (typeName != "DateTime" && typeName != "DateTimeOffset")
            return;

        // Verify it's actually System.DateTime or System.DateTimeOffset
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (!IsSystemDateTimeOrOffset(symbol))
            return;

        var fullName = $"{typeName}.{memberName}";
        var diag = Diagnostic.Create(
            DiagnosticDescriptors.AvoidDateTimeNow,
            memberAccess.GetLocation(),
            fullName);

        context.ReportDiagnostic(diag);
    }

    private static bool IsSystemDateTime(ISymbol? symbol)
    {
        if (symbol is not IPropertySymbol property)
            return false;

        var containingType = property.ContainingType;
        return containingType?.Name == "DateTime" &&
               containingType.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static bool IsSystemDateTimeOrOffset(ISymbol? symbol)
    {
        if (symbol is not IPropertySymbol property)
            return false;

        var containingType = property.ContainingType;
        if (containingType is null)
            return false;

        var namespaceName = containingType.ContainingNamespace?.ToDisplayString();
        if (namespaceName != "System")
            return false;

        return containingType.Name == "DateTime" || containingType.Name == "DateTimeOffset";
    }
}

// =============================================================================
// Pragmatic.Temporal.Analyzers - DateTime Without Kind Analyzer
// Detects new DateTime() calls that do not specify DateTimeKind
// =============================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Temporal.Analyzers;

/// <summary>
///     Analyzer that detects creation of DateTime without specifying DateTimeKind.
///     Constructors like new DateTime(year, month, day) default to Kind == Unspecified,
///     which can cause subtle timezone bugs.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DateTimeWithoutKindAnalyzer : DiagnosticAnalyzer
{
    // DateTime constructors that include DateTimeKind have these parameter counts:
    // new DateTime(long ticks, DateTimeKind kind) - 2 params
    // new DateTime(int y, int m, int d, DateTimeKind kind) - 4 params (does not exist - custom check needed)
    // Actually, the overloads with DateTimeKind are:
    //   DateTime(Int64, DateTimeKind)                    - 2 params
    //   DateTime(Int32, Int32, Int32, Int32, Int32, Int32, DateTimeKind) - 7 params
    //   DateTime(Int32, Int32, Int32, Int32, Int32, Int32, Int32, DateTimeKind) - 8 params
    //   DateTime(Int32, Int32, Int32, Calendar)          - has Calendar, not Kind
    //   DateTime(Int32, Int32, Int32, Int32, Int32, Int32, Int32, Calendar, DateTimeKind) - 9 params
    // So we check if the last parameter is DateTimeKind or Calendar-then-DateTimeKind.

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DiagnosticDescriptors.AvoidDateTimeWithoutKind);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        // Must have arguments (parameterless DateTime() is default(DateTime) essentially)
        if (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0)
            return;

        // Resolve the constructor being called
        var symbolInfo = context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol constructor)
            return;

        // Must be System.DateTime constructor
        var containingType = constructor.ContainingType;
        if (containingType is null ||
            containingType.Name != "DateTime" ||
            containingType.ContainingNamespace?.ToDisplayString() != "System")
            return;

        // Check if any parameter is DateTimeKind
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.Type.Name == "DateTimeKind" &&
                parameter.Type.ContainingNamespace?.ToDisplayString() == "System")
                return; // Has DateTimeKind parameter — safe
        }

        // DateTime constructor without DateTimeKind — report diagnostic
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.AvoidDateTimeWithoutKind,
            creation.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }
}

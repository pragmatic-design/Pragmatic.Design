using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Persistence.Analyzers;

/// <summary>
///     Blocks <c>default(Entity)</c> and <c>default</c> literal when the target type is an entity.
///     Entity types must be created via the generated <c>Create()</c> factory to ensure
///     proper ID generation, audit timestamps, and default values.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DoNotDefaultEntityAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.DoNotDefaultEntity);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeDefaultExpression,
            SyntaxKind.DefaultExpression,          // default(T)
            SyntaxKind.DefaultLiteralExpression);   // default
    }

    private static void AnalyzeDefaultExpression(SyntaxNodeAnalysisContext context)
    {
        INamedTypeSymbol? typeSymbol = null;

        switch (context.Node)
        {
            // default(Entity) — explicit form
            case DefaultExpressionSyntax defaultExpr:
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(defaultExpr, context.CancellationToken);
                typeSymbol = typeInfo.Type as INamedTypeSymbol;
                break;
            }
            // default — implicit form, resolve from converted type
            case LiteralExpressionSyntax:
            {
                var typeInfo = context.SemanticModel.GetTypeInfo(context.Node, context.CancellationToken);
                typeSymbol = typeInfo.ConvertedType as INamedTypeSymbol;
                break;
            }
        }

        if (typeSymbol is null || !EntityFilter.IsTarget(typeSymbol))
            return;

        // Suppress contexts where `default` does NOT create an entity instance to be used:
        //   x == default / x != default   → reference null-check
        //   void M(Order o = default)      → optional-parameter default (a signature, not creation)
        //   return default; / => default   → returning null, not constructing
        // Reporting these is noise, and the paired code fix would rewrite them into broken code
        // (e.g. `x == Order.Create()`, or a method call in a parameter default), so exclude them.
        if (IsNonCreationContext(context.Node))
            return;

        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.DoNotDefaultEntity,
            context.Node.GetLocation(),
            typeSymbol.Name);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsNonCreationContext(SyntaxNode node)
        => node.Parent switch
        {
            BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.EqualsExpression) || binary.IsKind(SyntaxKind.NotEqualsExpression)
                => true,
            ReturnStatementSyntax => true,
            ArrowExpressionClauseSyntax => true,
            EqualsValueClauseSyntax { Parent: ParameterSyntax } => true,
            _ => false
        };
}

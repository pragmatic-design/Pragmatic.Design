using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Pragmatic.Result.Analyzers;

/// <summary>
///     Reports <c>PRAG0001</c> when a Result type's <c>.Value</c> is accessed without first
///     proving success. Accessing <c>Value</c> on a failed result throws
///     <see cref="System.InvalidOperationException"/> at runtime, so the analyzer flags any access
///     that is not shielded by an <c>IsSuccess</c>/<c>IsFailure</c> check — an enclosing
///     <c>if</c>/ternary guard, an <c>is { IsSuccess: true }</c> pattern, or the early-return
///     guard-clause idiom (<c>if (result.IsFailure) return …;</c>). Recognizes aliased variables so
///     that <c>var r = result; r.Value</c> is analyzed against the guard on <c>r</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ResultValueAccessAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.UnsafeValueAccess);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeMemberAccess, SyntaxKind.SimpleMemberAccessExpression);
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Only interested in .Value access
        if (memberAccess.Name.Identifier.Text != "Value")
            return;

        // Get the type of the expression before .Value
        var typeInfo = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken);
        var type = typeInfo.Type;
        if (type is null)
            return;

        // Check if it's a Pragmatic.Result type
        if (!IsResultType(type))
            return;

        // Resolve the symbol of the expression to track aliases
        var symbol = GetUnderlyingSymbol(context.SemanticModel, memberAccess.Expression, context.CancellationToken);

        // Check if it's guarded by IsSuccess/IsFailure (enclosing if/ternary)
        if (IsGuardedAccess(context.SemanticModel, memberAccess, symbol, context.CancellationToken))
            return;

        // Check the idiomatic early-return guard: `if (x.IsFailure) return x; ... x.Value`
        // where the failure path exits before .Value is ever reached.
        if (IsGuardedByEarlyExit(context.SemanticModel, memberAccess, symbol, context.CancellationToken))
            return;

        // Report diagnostic
        var expressionText = memberAccess.Expression.ToString();
        var diagnostic = Diagnostic.Create(
            DiagnosticDescriptors.UnsafeValueAccess,
            memberAccess.Name.GetLocation(),
            expressionText);

        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsResultType(ITypeSymbol type)
    {
        // Hot path: the receiver of .Value is almost always Result/VoidResult itself.
        // Short-circuit on the type's own name + namespace before paying for the full
        // base-type + interface walk below (this analyzer runs per member-access node).
        if (type.OriginalDefinition is { Name: "Result" or "VoidResult" } self
            && self.ContainingNamespace?.ToDisplayString() == "Pragmatic.Result")
            return true;

        // Fallback: walk up the type hierarchy for subclasses / IResultBase implementers.
        var current = type;
        while (current != null)
        {
            if (current.ContainingNamespace?.ToDisplayString() == "Pragmatic.Result")
            {
                var name = current.OriginalDefinition.Name;
                if (name is "Result" or "VoidResult")
                    return true;
            }

            // Check if it implements IResultBase
            foreach (var iface in current.AllInterfaces)
            {
                if (iface.Name == "IResultBase"
                    && iface.ContainingNamespace?.ToDisplayString() == "Pragmatic.Result")
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    ///     Resolves the underlying symbol for an expression, handling aliases like
    ///     <c>var r = result;</c> where we need to track back to the original variable.
    /// </summary>
    private static ISymbol? GetUnderlyingSymbol(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
    }

    private static bool IsGuardedAccess(
        SemanticModel semanticModel,
        SyntaxNode node,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        var current = node.Parent;
        while (current != null)
        {
            switch (current)
            {
                case IfStatementSyntax ifStatement:
                    if (ConditionChecksSuccess(semanticModel, ifStatement.Condition, targetSymbol, cancellationToken))
                    {
                        if (IsInTrueBranch(ifStatement, node))
                            return true;
                    }

                    if (ConditionChecksFailure(semanticModel, ifStatement.Condition, targetSymbol, cancellationToken))
                    {
                        if (IsInElseBranch(ifStatement, node))
                            return true;
                    }

                    // if (!result.IsFailure) — negated failure = success
                    if (ConditionChecksNegatedFailure(semanticModel, ifStatement.Condition, targetSymbol,
                            cancellationToken))
                    {
                        if (IsInTrueBranch(ifStatement, node))
                            return true;
                    }

                    break;

                case ConditionalExpressionSyntax ternary:
                    if (ConditionChecksSuccess(semanticModel, ternary.Condition, targetSymbol, cancellationToken) ||
                        ConditionChecksNegatedFailure(semanticModel, ternary.Condition, targetSymbol,
                            cancellationToken))
                    {
                        if (ternary.WhenTrue.Contains(node))
                            return true;
                    }

                    break;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    ///     Checks if a condition expression references IsSuccess on the same symbol.
    ///     Handles: <c>result.IsSuccess</c>, <c>result is { IsSuccess: true }</c>.
    /// </summary>
    private static bool ConditionChecksSuccess(
        SemanticModel semanticModel,
        ExpressionSyntax condition,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        // Direct: result.IsSuccess
        if (IsPropertyAccessOnSymbol(semanticModel, condition, "IsSuccess", targetSymbol, cancellationToken))
            return true;

        // Pattern matching: result is { IsSuccess: true }
        if (IsPropertyPatternTrue(semanticModel, condition, "IsSuccess", targetSymbol, cancellationToken))
            return true;

        return false;
    }

    /// <summary>
    ///     Checks if a condition expression references IsFailure on the same symbol.
    /// </summary>
    private static bool ConditionChecksFailure(
        SemanticModel semanticModel,
        ExpressionSyntax condition,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (IsPropertyAccessOnSymbol(semanticModel, condition, "IsFailure", targetSymbol, cancellationToken))
            return true;

        if (IsPropertyPatternTrue(semanticModel, condition, "IsFailure", targetSymbol, cancellationToken))
            return true;

        return false;
    }

    /// <summary>
    ///     Recognizes the early-return (guard-clause) idiom: a preceding
    ///     <c>if (x.IsFailure) return …;</c> (or <c>if (!x.IsSuccess) return/throw …;</c>) on the same
    ///     symbol whose body always exits the method. After such a guard, <c>x.Value</c> is only reached on
    ///     the success path, so it must not be flagged. Walks preceding sibling statements outward through
    ///     enclosing blocks.
    /// </summary>
    private static bool IsGuardedByEarlyExit(
        SemanticModel semanticModel,
        SyntaxNode node,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (targetSymbol is null)
            return false;

        var statement = node.FirstAncestorOrSelf<StatementSyntax>();
        while (statement is not null)
        {
            if (statement.Parent is BlockSyntax block)
            {
                foreach (var prior in block.Statements)
                {
                    if (prior == statement)
                        break;

                    if (prior is IfStatementSyntax { Else: null } guard
                        && StatementAlwaysExits(guard.Statement)
                        && (ConditionChecksFailure(semanticModel, guard.Condition, targetSymbol, cancellationToken)
                            || ConditionChecksNegatedSuccess(semanticModel, guard.Condition, targetSymbol, cancellationToken)))
                    {
                        return true;
                    }
                }
            }

            statement = statement.Parent?.FirstAncestorOrSelf<StatementSyntax>();
        }

        return false;
    }

    /// <summary>True if the statement unconditionally leaves the method (return/throw, or a block ending so).</summary>
    private static bool StatementAlwaysExits(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax => true,
        ThrowStatementSyntax => true,
        BlockSyntax block => block.Statements.Count > 0 && StatementAlwaysExits(block.Statements[block.Statements.Count - 1]),
        _ => false,
    };

    /// <summary>Checks for <c>!result.IsSuccess</c> — negated success = failure-guard.</summary>
    private static bool ConditionChecksNegatedSuccess(
        SemanticModel semanticModel,
        ExpressionSyntax condition,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation)
            return ConditionChecksSuccess(semanticModel, negation.Operand, targetSymbol, cancellationToken);

        return false;
    }

    /// <summary>
    ///     Checks for <c>!result.IsFailure</c> — negated failure = success.
    /// </summary>
    private static bool ConditionChecksNegatedFailure(
        SemanticModel semanticModel,
        ExpressionSyntax condition,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (condition is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalNotExpression } negation)
        {
            return ConditionChecksFailure(semanticModel, negation.Operand, targetSymbol, cancellationToken);
        }

        return false;
    }

    /// <summary>
    ///     Checks if the expression is a member access like <c>symbol.propertyName</c>
    ///     where the receiver resolves to the same symbol as targetSymbol.
    /// </summary>
    private static bool IsPropertyAccessOnSymbol(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        string propertyName,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (memberAccess.Name.Identifier.Text != propertyName)
            return false;

        if (targetSymbol is null)
            return false;

        var receiverSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol;
        return SymbolEqualityComparer.Default.Equals(receiverSymbol, targetSymbol);
    }

    /// <summary>
    ///     Checks if the expression matches pattern: <c>symbol is { PropertyName: true }</c>.
    /// </summary>
    private static bool IsPropertyPatternTrue(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        string propertyName,
        ISymbol? targetSymbol,
        CancellationToken cancellationToken)
    {
        if (expression is not IsPatternExpressionSyntax isPattern)
            return false;

        if (targetSymbol is null)
            return false;

        // Check the left side resolves to the same symbol
        var leftSymbol = semanticModel.GetSymbolInfo(isPattern.Expression, cancellationToken).Symbol;
        if (!SymbolEqualityComparer.Default.Equals(leftSymbol, targetSymbol))
            return false;

        // Check the pattern is a recursive pattern with the property
        if (isPattern.Pattern is not RecursivePatternSyntax recursivePattern)
            return false;

        if (recursivePattern.PropertyPatternClause is null)
            return false;

        foreach (var subPattern in recursivePattern.PropertyPatternClause.Subpatterns)
        {
            if (subPattern.NameColon?.Name.Identifier.Text == propertyName
                && subPattern.Pattern is ConstantPatternSyntax constantPattern
                && constantPattern.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInTrueBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        return ifStatement.Statement.Contains(node);
    }

    private static bool IsInElseBranch(IfStatementSyntax ifStatement, SyntaxNode node)
    {
        return ifStatement.Else?.Statement.Contains(node) == true;
    }
}

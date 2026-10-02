using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Temporal.CodeFixers;

/// <summary>
///     Provides a code fix for PRAG0903: rewrites a relational comparison between two
///     <c>DateTimeOffset</c> values to compare their <c>.UtcDateTime</c> explicitly.
/// </summary>
/// <remarks>
///     The analyzer fires when either operand is a DateTimeOffset; the fix is only offered
///     when BOTH are, because appending <c>.UtcDateTime</c> to a plain DateTime operand
///     would not compile and wrapping only one side would silently change the comparison.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeOffsetComparisonCodeFixProvider))]
[Shared]
public sealed class DateTimeOffsetComparisonCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("PRAG0903");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var comparison = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<BinaryExpressionSyntax>();
        if (comparison is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        if (!IsDateTimeOffset(semanticModel, comparison.Left, context.CancellationToken) ||
            !IsDateTimeOffset(semanticModel, comparison.Right, context.CancellationToken))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: "Compare using .UtcDateTime",
                createChangedDocument: ct => WrapOperandsAsync(context.Document, comparison, ct),
                equivalenceKey: "PRAG0903_UtcDateTime"),
            diagnostic);
    }

    private static bool IsDateTimeOffset(SemanticModel semanticModel, ExpressionSyntax expression, CancellationToken cancellationToken)
    {
        var type = semanticModel.GetTypeInfo(expression, cancellationToken).Type;
        return type is { Name: "DateTimeOffset" } &&
               type.ContainingNamespace?.ToDisplayString() == "System";
    }

    private static async Task<Document> WrapOperandsAsync(
        Document document, BinaryExpressionSyntax comparison, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var newComparison = comparison
            .WithLeft(WrapWithUtcDateTime(comparison.Left))
            .WithRight(WrapWithUtcDateTime(comparison.Right));

        var newRoot = root.ReplaceNode(comparison, newComparison);
        return document.WithSyntaxRoot(newRoot);
    }

    private static ExpressionSyntax WrapWithUtcDateTime(ExpressionSyntax operand)
    {
        // Member access binds tighter than any expression that could appear as a relational
        // operand, so anything that is not already a primary expression needs parentheses.
        var receiver = operand switch
        {
            IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax
                or ElementAccessExpressionSyntax or ParenthesizedExpressionSyntax => operand.WithoutTrivia(),
            _ => SyntaxFactory.ParenthesizedExpression(operand.WithoutTrivia())
        };

        return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                SyntaxFactory.IdentifierName("UtcDateTime"))
            .WithTriviaFrom(operand);
    }
}

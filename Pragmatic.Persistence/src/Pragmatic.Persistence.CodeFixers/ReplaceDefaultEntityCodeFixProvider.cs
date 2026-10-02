using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.CodeFixers;

/// <summary>
///     Provides a code fix for PRAG0681: replaces <c>default(Entity)</c> or <c>default</c>
///     with <c>Entity.Create()</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReplaceDefaultEntityCodeFixProvider))]
[Shared]
public sealed class ReplaceDefaultEntityCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("PRAG0681");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var node = root.FindNode(diagnosticSpan);

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        string? typeName = null;

        switch (node)
        {
            // default(Entity) — explicit form
            case DefaultExpressionSyntax defaultExpr:
            {
                var typeInfo = semanticModel.GetTypeInfo(defaultExpr, context.CancellationToken);
                typeName = typeInfo.Type?.Name;
                break;
            }
            // default — implicit form
            case LiteralExpressionSyntax:
            {
                var typeInfo = semanticModel.GetTypeInfo(node, context.CancellationToken);
                typeName = typeInfo.ConvertedType?.Name;
                break;
            }
        }

        if (typeName is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with {typeName}.Create()",
                createChangedDocument: ct => ReplaceWithCreateAsync(context.Document, node, typeName, ct),
                equivalenceKey: "PRAG0681_ReplaceWithCreate"),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithCreateAsync(
        Document document, SyntaxNode node, string typeName, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var createCall = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(typeName),
                SyntaxFactory.IdentifierName("Create")));

        var newRoot = root.ReplaceNode(node, createCall.WithTriviaFrom(node));
        return document.WithSyntaxRoot(newRoot);
    }
}

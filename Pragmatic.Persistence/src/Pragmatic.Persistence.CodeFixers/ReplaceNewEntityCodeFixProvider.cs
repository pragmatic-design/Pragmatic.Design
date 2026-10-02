using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Persistence.CodeFixers;

/// <summary>
///     Provides a code fix for PRAG0680: replaces <c>new Entity()</c> with <c>Entity.Create()</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReplaceNewEntityCodeFixProvider))]
[Shared]
public sealed class ReplaceNewEntityCodeFixProvider : CodeFixProvider
{
    /// <summary>
    ///     Namespace-qualified type name (no <c>global::</c> prefix). Types in the global
    ///     namespace render as their bare name; namespaced types render as <c>Ns.Type</c>.
    /// </summary>
    private static readonly SymbolDisplayFormat QualifiedNameFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("PRAG0680");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var node = root.FindNode(diagnosticSpan);

        if (node is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
            return;

        // Do not offer the fix when the creation carries constructor arguments or an object
        // initializer: the generated Create() factory has its own signature, so blindly emitting
        // Create() would silently drop them (an object initializer cannot bind to a method call at
        // all). Leave the diagnostic for the developer rather than produce lossy/broken code.
        if (HasArgumentsOrInitializer(node))
            return;

        // Resolve the namespace-qualified type name from the symbol so the generated
        // "{Type}.Create()" call is unambiguous regardless of usings or short-name aliasing.
        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        var typeSymbol = semanticModel.GetTypeInfo(node, context.CancellationToken).Type;
        if (typeSymbol is null) return;

        // Display name used in the action title (short, friendly).
        var displayName = typeSymbol.Name;

        // Namespace-qualified name used in the emitted code (e.g. My.Ns.Order), so the generated
        // "{Type}.Create()" call binds unambiguously even when the short name is aliased or shared.
        // A type in the global namespace yields just its bare name.
        var qualifiedName = typeSymbol.ToDisplayString(QualifiedNameFormat);

        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with {displayName}.Create()",
                createChangedDocument: ct => ReplaceWithCreateAsync(context.Document, node, qualifiedName, ct),
                equivalenceKey: "PRAG0680_ReplaceWithCreate"),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithCreateAsync(
        Document document, SyntaxNode node, string qualifiedTypeName, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // Generate: Namespace.TypeName.Create()
        // ParseExpression turns the dotted, namespace-qualified name into a proper qualified-name
        // expression; a plain IdentifierName would treat the dots as part of a single identifier.
        var typeExpression = SyntaxFactory.ParseExpression(qualifiedTypeName);

        var createCall = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                typeExpression,
                SyntaxFactory.IdentifierName("Create")));

        var newRoot = root.ReplaceNode(node, createCall.WithTriviaFrom(node));
        return document.WithSyntaxRoot(newRoot);
    }

    private static bool HasArgumentsOrInitializer(SyntaxNode node)
        => node switch
        {
            ObjectCreationExpressionSyntax o => o.ArgumentList?.Arguments.Count > 0 || o.Initializer is not null,
            ImplicitObjectCreationExpressionSyntax i => i.ArgumentList?.Arguments.Count > 0 || i.Initializer is not null,
            _ => false
        };
}

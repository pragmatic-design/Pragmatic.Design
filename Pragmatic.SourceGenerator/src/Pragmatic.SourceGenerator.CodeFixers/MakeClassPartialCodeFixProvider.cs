using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.CodeFixers;

/// <summary>
///     Provides a code fix that adds the <c>partial</c> modifier to classes that require it
///     for Pragmatic source generation. Handles all "must be partial" diagnostics across modules.
/// </summary>
/// <remarks>
///     Covers: Actions (PRAG0400, PRAG0406), Endpoints (PRAG0500), Persistence (PRAG0600, PRAG0602, PRAG0712),
///     Messaging (PRAG0801), Ownership (PRAG1100), Jobs (PRAG2502), Validation (PRAG0200),
///     Mapping (PRAG0300), Caching (PRAG1700), Patch (PRAG2200), Configuration (PRAG2000).
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MakeClassPartialCodeFixProvider))]
[Shared]
public sealed class MakeClassPartialCodeFixProvider : CodeFixProvider
{
    private const string Title = "Make class partial";

    /// <summary>
    ///     All "must be partial" diagnostic IDs across Pragmatic modules.
    /// </summary>
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(
        "PRAG0200", // Validation
        "PRAG0300", // Mapping
        "PRAG0400", // Actions (Mutation/DomainAction)
        "PRAG0406", // Actions (Boundary)
        "PRAG0500", // Endpoints
        "PRAG0600", // Persistence (Entity/Repository)
        "PRAG0602", // Persistence (Database)
        "PRAG0712", // Persistence (Query)
        "PRAG0801", // Messaging (MessageHandler)
        "PRAG1100", // Ownership (OwnedEntity)
        "PRAG1700", // Caching
        "PRAG2200", // Patch
        "PRAG2000", // Configuration
        "PRAG2502"  // Jobs
    );

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var diagnosticSpan = diagnostic.Location.SourceSpan;
        var node = root.FindNode(diagnosticSpan);

        // Walk up to find the enclosing type declaration
        var typeDecl = node.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        if (typeDecl is null) return;

        // Only offer fix if type is NOT already partial
        if (typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                title: Title,
                createChangedDocument: ct => MakePartialAsync(context.Document, typeDecl, ct),
                equivalenceKey: "MakeClassPartial"),
            diagnostic);
    }

    private static async Task<Document> MakePartialAsync(
        Document document, TypeDeclarationSyntax typeDecl, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        // Insert 'partial' before the class/struct/record keyword, preserving existing trivia
        var partialToken = SyntaxFactory.Token(SyntaxKind.PartialKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        var newModifiers = typeDecl.Modifiers.Add(partialToken);
        var newTypeDecl = typeDecl.WithModifiers(newModifiers);

        var newRoot = root.ReplaceNode(typeDecl, newTypeDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}

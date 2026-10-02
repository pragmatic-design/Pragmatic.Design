using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Temporal.CodeFixers;

/// <summary>
///     Provides code fixes for PRAG0902: adds an explicit <c>DateTimeKind</c> argument to
///     <c>new DateTime(...)</c> calls that omit it. Offers Utc (preferred), Local, and Unspecified.
/// </summary>
/// <remarks>
///     DateTime only has Kind-accepting overloads at specific arities: (ticks, kind),
///     (y,m,d,h,mi,s,kind), (y,m,d,h,mi,s,ms,kind) and (y,m,d,h,mi,s,ms,us,kind).
///     The common 3-argument date-only constructor therefore expands to the 7-argument
///     overload with a midnight time. Calendar-based constructors are skipped: inserting
///     a kind there would require reordering arguments, which is not a safe automated fix.
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeWithoutKindCodeFixProvider))]
[Shared]
public sealed class DateTimeWithoutKindCodeFixProvider : CodeFixProvider
{
    private static readonly ImmutableArray<string> Kinds =
        ImmutableArray.Create("Utc", "Local", "Unspecified");

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("PRAG0902");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var creation = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<ObjectCreationExpressionSyntax>();
        if (creation?.ArgumentList is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel?.GetSymbolInfo(creation, context.CancellationToken).Symbol is not IMethodSymbol constructor)
            return;

        // Calendar-based constructors have no "append the kind" shape — skip.
        foreach (var parameter in constructor.Parameters)
        {
            if (parameter.Type.Name == "Calendar")
                return;
        }

        // Only arities with a Kind-accepting sibling overload are fixable.
        var argumentCount = creation.ArgumentList.Arguments.Count;
        if (argumentCount is not (1 or 3 or 6 or 7 or 8))
            return;

        foreach (var kind in Kinds)
        {
            var kindName = kind;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: $"Specify DateTimeKind.{kindName}",
                    createChangedDocument: ct => AddKindArgumentAsync(context.Document, creation, kindName, ct),
                    equivalenceKey: $"PRAG0902_{kindName}"),
                diagnostic);
        }
    }

    private static async Task<Document> AddKindArgumentAsync(
        Document document, ObjectCreationExpressionSyntax creation, string kind, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || creation.ArgumentList is null) return document;

        // Qualify DateTimeKind the same way the constructor's type is written, so the fix
        // compiles even in files that reference System.DateTime without a using directive.
        var kindExpression = creation.Type is QualifiedNameSyntax
            ? $"System.DateTimeKind.{kind}"
            : $"DateTimeKind.{kind}";

        var newArguments = creation.ArgumentList.Arguments;

        // new DateTime(y, m, d) has no 4-argument Kind overload: expand to the
        // 7-argument (y, m, d, h, mi, s, kind) overload with a midnight time.
        if (newArguments.Count == 3)
        {
            newArguments = newArguments
                .Add(SyntaxFactory.Argument(SyntaxFactory.ParseExpression("0")))
                .Add(SyntaxFactory.Argument(SyntaxFactory.ParseExpression("0")))
                .Add(SyntaxFactory.Argument(SyntaxFactory.ParseExpression("0")));
        }

        newArguments = newArguments.Add(SyntaxFactory.Argument(SyntaxFactory.ParseExpression(kindExpression)));

        var newCreation = creation.WithArgumentList(
            creation.ArgumentList.WithArguments(newArguments));

        var newRoot = root.ReplaceNode(creation, newCreation);
        return document.WithSyntaxRoot(newRoot);
    }
}

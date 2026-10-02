using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.Temporal.CodeFixers;

/// <summary>
///     Provides a code fix for PRAG0900/PRAG0901/PRAG0904: replaces a direct
///     <c>DateTime.Now</c>-family access with the equivalent read from an
///     <c>IClock</c> member already available on the containing type.
/// </summary>
/// <remarks>
///     <para>
///         The fix is only offered when the containing type actually exposes an
///         <c>Pragmatic.Temporal.Clock.IClock</c> (field, property, or primary-constructor
///         parameter) usable from the call site — the provider never invents a member or
///         guesses a name. Without a clock in scope there is no mechanical fix: injecting
///         one is a design decision that belongs to the developer.
///     </para>
///     <para>
///         Replacements are type- and Kind-correct:
///         <c>DateTime.Now</c> → <c>clock.Now.LocalDateTime</c> (DateTime, Kind.Local);
///         <c>DateTime.UtcNow</c> → <c>clock.UtcNow.UtcDateTime</c> (DateTime, Kind.Utc);
///         <c>DateTime.Today</c> → <c>clock.Now.LocalDateTime.Date</c> (local midnight,
///         Kind.Local — <c>IClock.Today</c> is a DateOnly, and DateOnly.ToDateTime would
///         lose the Kind that DateTime.Today carries);
///         <c>DateTimeOffset.Now</c> → <c>clock.Now</c>;
///         <c>DateTimeOffset.UtcNow</c> → <c>clock.UtcNow</c>.
///     </para>
/// </remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DateTimeNowCodeFixProvider))]
[Shared]
public sealed class DateTimeNowCodeFixProvider : CodeFixProvider
{
    private const string ClockInterfaceFullName = "Pragmatic.Temporal.Clock.IClock";

    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create("PRAG0900", "PRAG0901", "PRAG0904");

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var memberAccess = root.FindNode(diagnostic.Location.SourceSpan)
            .FirstAncestorOrSelf<MemberAccessExpressionSyntax>();
        if (memberAccess is null) return;

        // Which accessor is being replaced determines the IClock expression shape.
        if (memberAccess.Expression is not IdentifierNameSyntax typeIdentifier)
            return;

        var replacementSuffix = (typeIdentifier.Identifier.Text, memberAccess.Name.Identifier.Text) switch
        {
            ("DateTime", "Now") => ".Now.LocalDateTime",
            ("DateTime", "UtcNow") => ".UtcNow.UtcDateTime",
            ("DateTime", "Today") => ".Now.LocalDateTime.Date",
            ("DateTimeOffset", "Now") => ".Now",
            ("DateTimeOffset", "UtcNow") => ".UtcNow",
            _ => null
        };
        if (replacementSuffix is null) return;

        var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (semanticModel is null) return;

        var clockMember = FindAccessibleClockMember(semanticModel, memberAccess);
        if (clockMember is null) return;

        var replacement = clockMember + replacementSuffix;
        context.RegisterCodeFix(
            CodeAction.Create(
                title: $"Replace with {replacement}",
                createChangedDocument: ct => ReplaceAsync(context.Document, memberAccess, replacement, ct),
                equivalenceKey: $"PRAG0900_{replacementSuffix}"),
            diagnostic);
    }

    /// <summary>
    ///     Finds an IClock member of the containing type reachable from the call site:
    ///     instance fields/properties/primary-constructor parameters when the enclosing
    ///     member is an instance member, static fields/properties always.
    /// </summary>
    private static string? FindAccessibleClockMember(SemanticModel semanticModel, SyntaxNode node)
    {
        var enclosing = semanticModel.GetEnclosingSymbol(node.SpanStart);
        if (enclosing is null) return null;

        // Lambdas and local functions inherit the instance context of the member they live in.
        var enclosingMember = enclosing;
        while (enclosingMember is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
            enclosingMember = enclosingMember.ContainingSymbol;

        var containingType = enclosingMember.ContainingType;
        if (containingType is null) return null;

        var instanceContext = !enclosingMember.IsStatic;

        foreach (var member in containingType.GetMembers())
        {
            switch (member)
            {
                // Skip compiler-generated members (auto-property backing fields and the like).
                case IFieldSymbol field when !field.IsImplicitlyDeclared &&
                                             IsClock(field.Type) && (field.IsStatic || instanceContext):
                    return field.Name;
                case IPropertySymbol { GetMethod: not null } property
                    when IsClock(property.Type) && (property.IsStatic || instanceContext):
                    return property.Name;
            }
        }

        if (!instanceContext) return null;

        // Primary-constructor parameters are captured and usable in instance member bodies,
        // but they do not surface in GetMembers() — probe the constructors declared on the
        // type declaration itself.
        foreach (var constructor in containingType.InstanceConstructors)
        {
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not TypeDeclarationSyntax)
                    continue;

                foreach (var parameter in constructor.Parameters)
                {
                    if (IsClock(parameter.Type))
                        return parameter.Name;
                }
            }
        }

        return null;
    }

    private static bool IsClock(ITypeSymbol type) =>
        type.Name == "IClock" &&
        type.ContainingNamespace?.ToDisplayString() == "Pragmatic.Temporal.Clock";

    private static async Task<Document> ReplaceAsync(
        Document document, MemberAccessExpressionSyntax memberAccess, string replacement, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null) return document;

        var replacementExpression = SyntaxFactory.ParseExpression(replacement)
            .WithTriviaFrom(memberAccess);

        var newRoot = root.ReplaceNode(memberAccess, replacementExpression);
        return document.WithSyntaxRoot(newRoot);
    }
}

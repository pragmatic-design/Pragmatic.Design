using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The <c>Expression</c> of <c>[Sum]</c>, <c>[Avg]</c>, <c>[Min]</c> or <c>[Max]</c>, written over the
///     row the aggregate reads.
/// </summary>
/// <remarks>
///     <para>
///         The attribute takes the expression written against the entity — <c>"Quantity * UnitPrice"</c>
///         — and the aggregate needs it against the row: <c>x.Quantity * x.UnitPrice</c>. Pasting it after
///         <c>x.</c> reached the first member only, and the rest was a <c>CS0103</c> inside the
///         generated view.
///     </para>
///     <para>
///         The expression is a string, so there is no semantic model to bind it with. A name that stands
///         alone is the entity's when the entity has, or will have, a member of that name; anything else
///         — a literal, a type, a name after a dot — is left as written. A <c>[Projectable]</c> member
///         becomes its body: its getter is no column, and EF Core cannot translate it in an aggregate.
///     </para>
/// </remarks>
internal static class AggregateExpression
{
    /// <summary>The row parameter of the generated aggregate lambda: <c>g.Sum(x =&gt; …)</c>.</summary>
    public const string Row = "x";

    /// <summary>
    ///     <paramref name="expression" /> over <see cref="Row" />; an expression that does not parse is
    ///     prefixed as written, so the compiler reports it as it always did.
    /// </summary>
    public static string Over(string expression, ITypeSymbol entity, Compilation compilation)
    {
        var parsed = SyntaxFactory.ParseExpression(expression);
        if (parsed.ContainsDiagnostics)
            return $"{Row}.{expression}";

        return new RowRewriter(entity, compilation).Visit(parsed)?.ToString() ?? $"{Row}.{expression}";
    }

    private sealed class RowRewriter(ITypeSymbol entity, Compilation compilation) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!StandsAlone(node) || QueryViewMembers.Find(entity, node.Identifier.ValueText) is not { } member)
                return base.VisitIdentifierName(node);

            if (member.Declared is { } declared && ProjectableBody.Of(declared, compilation, Row) is { } body)
                return SyntaxFactory.ParenthesizedExpression(SyntaxFactory.ParseExpression(body)).WithTriviaFrom(node);

            return SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName(Row),
                    node.WithoutTrivia())
                .WithTriviaFrom(node);
        }

        /// <summary>Not the name after a dot, nor the name of a named argument.</summary>
        private static bool StandsAlone(IdentifierNameSyntax node) => node.Parent switch
        {
            MemberAccessExpressionSyntax memberAccess => !ReferenceEquals(memberAccess.Name, node),
            NameColonSyntax or NameEqualsSyntax => false,
            _ => true
        };
    }
}

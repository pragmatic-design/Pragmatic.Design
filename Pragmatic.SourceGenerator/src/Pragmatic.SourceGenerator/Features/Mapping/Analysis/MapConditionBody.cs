using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     The body of a <c>[MapCondition]</c> predicate, written over the projection's row, so the projection
///     can gate the property as <c>FromEntity</c> does.
/// </summary>
/// <remarks>
///     <para>
///         The predicate is <c>static bool IsDecided(LeaveRequest r) =&gt; r.Status != …</c>. Its parameter
///         becomes the row, and a type it names is written fully qualified, since the generated file does
///         not share the DTO's usings. What is left — the DTO's own static members, literals — reads the
///         same in the generated partial of the same DTO.
///     </para>
///     <para>
///         Only an expression body can be inlined. A block body returns null and the projection maps the
///         property unconditionally, which PRAG0332 still says.
///     </para>
/// </remarks>
internal static class MapConditionBody
{
    /// <summary>
    ///     The body of <paramref name="methodName" /> on <paramref name="dto" /> over
    ///     <paramref name="source" />, or null when it is not a single-parameter method with an
    ///     expression body in this compilation.
    /// </summary>
    public static string? Of(INamedTypeSymbol dto, string methodName, Compilation compilation, string source)
    {
        foreach (var method in dto.GetMembers(methodName).OfType<IMethodSymbol>())
        {
            if (method.Parameters.Length != 1)
                continue;

            foreach (var reference in method.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is not MethodDeclarationSyntax { ExpressionBody: { } body }
                    || !compilation.ContainsSyntaxTree(reference.SyntaxTree))
                    continue;

                var model = compilation.GetSemanticModel(reference.SyntaxTree);
                return new RowRewriter(model, method.Parameters[0], source).Visit(body.Expression)?.ToString();
            }
        }

        return null;
    }

    private sealed class RowRewriter(SemanticModel model, IParameterSymbol parameter, string source)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            var symbol = model.GetSymbolInfo(node).Symbol;

            if (SymbolEqualityComparer.Default.Equals(symbol, parameter))
                return SyntaxFactory.IdentifierName(source).WithTriviaFrom(node);

            if (symbol is INamedTypeSymbol type
                && node.Parent is MemberAccessExpressionSyntax access
                && ReferenceEquals(access.Expression, node))
                return SyntaxFactory.IdentifierName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .WithTriviaFrom(node);

            return base.VisitIdentifierName(node);
        }
    }
}

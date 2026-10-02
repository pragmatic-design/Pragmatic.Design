using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     Rewrites every type name inside a copied initializer to its fully qualified form.
/// </summary>
/// <remarks>
///     <para>
///         An initializer is written in the author's file and read in a generated one. The generated
///         file carries the action's <b>namespace</b> and none of its <c>using</c> directives, so a
///         name the author resolved through an import does not bind there: <c>CS0103</c>, on a line
///         that in their own file is valid, inside a file they cannot open.
///     </para>
///     <para>
///         Only names that bind to a type are rewritten. A member name binds to a field or a
///         property, and rewriting it would produce something that is not a name at all.
///     </para>
/// </remarks>
internal sealed class InitializerRewriter(SemanticModel model) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        => Qualified(node) ?? base.VisitIdentifierName(node);

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
    {
        // The type arguments are rewritten too — List<Tone> needs both halves qualified — so the
        // generic name is handled by qualifying the whole node at once.
        return Qualified(node) ?? base.VisitGenericName(node);
    }

    /// <summary>The fully qualified form of a name that binds to a type, or null for anything else.</summary>
    private SyntaxNode? Qualified(SimpleNameSyntax node)
    {
        // The right-hand side of a member access is a member, not a type: Tone.Bold binds Bold to a
        // field. Rewriting it would strip the qualifier the expression already has.
        if (node.Parent is MemberAccessExpressionSyntax member && member.Name == node)
            return null;

        if (model.GetSymbolInfo(node).Symbol is not INamedTypeSymbol type)
            return null;

        var fqn = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return SyntaxFactory.ParseTypeName(fqn).WithTriviaFrom(node);
    }
}

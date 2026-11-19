using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Result.Models;

namespace Pragmatic.SourceGenerator.Features.Result.Transforms;

/// <summary>
///     Reads a C# 15 union that implements <c>IError</c> into a <see cref="UnionErrorModel" /> (#865).
/// </summary>
/// <remarks>
///     <para>
///         Structural, as option A of docs/design/csharp15-union-errors.md decides: the generator compiles against Roslyn 4.14, which has no
///         union API. A union is a struct that implements <c>System.Runtime.CompilerServices.IUnion</c>, and its cases are the parameter
///         types of its one-parameter constructors. A struct that implements <c>IUnion</c> by hand has the same shape and is read the same way.
///     </para>
///     <para>
///         The declaration itself is a <c>TypeDeclarationSyntax</c> in the compiler that runs the generator (Roslyn 5.x), of a kind Roslyn
///         4.14 cannot name. The kind is looked up by name on the compiler that loaded the generator, so no number is written here, and on a
///         compiler without unions the lookup fails and only the hand-written shape is read.
///     </para>
/// </remarks>
internal static class UnionErrorTransform
{
    private const string ErrorInterface = "Pragmatic.Result.IError";

    /// <summary>The raw kind of a union declaration on the running compiler; -1 when it has none.</summary>
    private static readonly int UnionDeclarationKind =
        System.Enum.TryParse<SyntaxKind>("UnionDeclaration", out var kind) ? (int)kind : -1;

    /// <summary>A declaration that can be a union: a union, or a struct that may implement <c>IUnion</c> by hand.</summary>
    public static bool IsCandidate(SyntaxNode node)
        => node is StructDeclarationSyntax || (UnionDeclarationKind >= 0 && node.RawKind == UnionDeclarationKind);

    /// <summary>The union error set the declaration is, or null when it is not a union or does not implement <c>IError</c>.</summary>
    public static UnionErrorModel? From(GeneratorSyntaxContext context)
    {
        var declaration = (TypeDeclarationSyntax)context.Node;
        if (ModelExtensions.GetDeclaredSymbol(context.SemanticModel, declaration) is not INamedTypeSymbol { TypeKind: TypeKind.Struct } union)
            return null;
        if (!UnionCases.IsUnion(union) || !Implements(union, ErrorInterface))
            return null;

        var nonErrors = UnionCases.Of(union)
            .Where(c => !Implements(c, ErrorInterface))
            .Select(c => c.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
            .ToImmutableArray();

        return new UnionErrorModel(
            union.ContainingNamespace.IsGlobalNamespace ? "" : union.ContainingNamespace.ToDisplayString(),
            union.Name,
            union.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            union.DeclaredAccessibility.ToString().ToLowerInvariant(),
            declaration.RawKind == UnionDeclarationKind,
            union.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax part
                                                     && part.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword))),
            union.ContainingType is not null,
            nonErrors,
            MissingMembers(union),
            LocationInfo.From(declaration.Identifier.GetLocation()));
    }

    /// <summary>The <c>IError</c> members the union does not declare itself.</summary>
    private static UnionErrorMembers MissingMembers(INamedTypeSymbol union)
    {
        var missing = UnionErrorMembers.All;
        foreach (var member in union.GetMembers())
        {
            if (member.IsStatic || member.IsImplicitlyDeclared)
                continue;
            missing &= ~(member switch
            {
                IPropertySymbol { Name: "Code" } => UnionErrorMembers.Code,
                IPropertySymbol { Name: "StatusCode" } => UnionErrorMembers.StatusCode,
                IPropertySymbol { Name: "Title" } => UnionErrorMembers.Title,
                IPropertySymbol { Name: "Description" } => UnionErrorMembers.Description,
                IMethodSymbol { Name: "WriteExtensions", Parameters.Length: 1 } => UnionErrorMembers.WriteExtensions,
                IMethodSymbol { Name: "WriteExtensions", Parameters.Length: 2 } => UnionErrorMembers.WriteResolvedExtensions,
                _ => UnionErrorMembers.None,
            });
        }

        return missing;
    }

    private static bool Implements(ITypeSymbol type, string interfaceName)
        => type.AllInterfaces.Any(i => i.ToDisplayString() == interfaceName);
}

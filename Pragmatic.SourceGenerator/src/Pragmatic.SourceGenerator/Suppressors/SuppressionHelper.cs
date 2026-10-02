using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Suppressors;

/// <summary>
///     Shared helper for DiagnosticSuppressors — determines whether a symbol belongs
///     to a Pragmatic-decorated type that has SG-generated partial code.
/// </summary>
internal static class SuppressionHelper
{
    private static readonly (string Namespace, string Name)[] PragmaticAttributes =
    {
        ("Pragmatic.Persistence.Entity", "EntityAttribute"),
        ("Pragmatic.Actions.Attributes", "DomainActionAttribute"),
        ("Pragmatic.Actions.Attributes", "MutationAttribute"),
        ("Pragmatic.Actions.Attributes", "QueryAttribute"),
        ("Pragmatic.Mapping.Attributes", "MapFromAttribute"),
        ("Pragmatic.Mapping.Attributes", "MapToAttribute")
    };

    /// <summary>
    ///     Checks whether the type has any Pragmatic attribute that triggers SG code generation.
    ///     Walks the type hierarchy to handle derived types.
    /// </summary>
    public static bool IsPragmaticType(INamedTypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if (attrClass is null)
                    continue;

                var name = attrClass.OriginalDefinition.Name;
                var ns = attrClass.ContainingNamespace?.ToDisplayString();

                foreach (var (expectedNs, expectedName) in PragmaticAttributes)
                {
                    if (name == expectedName && ns == expectedNs)
                        return true;
                }
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    ///     Checks whether the type specifically has [Entity] or [Entity].
    /// </summary>
    public static bool IsEntityType(INamedTypeSymbol? type)
    {
        var current = type;
        while (current != null)
        {
            foreach (var attribute in current.GetAttributes())
            {
                var attrClass = attribute.AttributeClass;
                if (attrClass is null)
                    continue;

                if (attrClass.OriginalDefinition.Name == "EntityAttribute" &&
                    attrClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity")
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    ///     Checks whether the type is declared <c>partial</c>. Every suppression here is justified by
    ///     "the generator contributes another half of this type"; without <c>partial</c> that half
    ///     cannot exist, so the diagnostic is about hand-written code only.
    /// </summary>
    public static bool IsPartialType(INamedTypeSymbol? type)
    {
        if (type is null)
            return false;

        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.IndexOf(SyntaxKind.PartialKeyword) >= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Checks whether the location belongs to a generated file (<c>*.g.cs</c> / <c>*.generated.cs</c>).
    /// </summary>
    public static bool IsGeneratedLocation(Location? location)
    {
        var path = location?.SourceTree?.FilePath;
        if (string.IsNullOrEmpty(path))
            return false;

        return path!.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Nearest enclosing type declaration symbol, or <c>null</c>.
    /// </summary>
    public static INamedTypeSymbol? FindContainingType(SyntaxNode? node, SemanticModel semanticModel)
    {
        var current = node;
        while (current != null)
        {
            if (current is TypeDeclarationSyntax typeDeclaration)
                return semanticModel.GetDeclaredSymbol(typeDeclaration);

            current = current.Parent;
        }

        return null;
    }

    /// <summary>
    ///     Symbol of the member the diagnostic falls on (property, field, method, …), stopping at the
    ///     enclosing type declaration.
    /// </summary>
    public static ISymbol? FindDeclaredMember(SyntaxNode? node, SemanticModel semanticModel)
    {
        var current = node;
        while (current != null)
        {
            switch (current)
            {
                case TypeDeclarationSyntax:
                    return null;
                case VariableDeclaratorSyntax variable:
                    return semanticModel.GetDeclaredSymbol(variable);
                case MemberDeclarationSyntax member:
                    return semanticModel.GetDeclaredSymbol(member);
            }

            current = current.Parent;
        }

        return null;
    }
}

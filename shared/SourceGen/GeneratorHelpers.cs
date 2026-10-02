// =============================================================================
// Pragmatic.Design - Generator Helpers
// Common utilities for IIncrementalGenerator implementations
// =============================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGen;

/// <summary>
///     Common predicates and transforms for incremental generators.
/// </summary>
internal static class GeneratorHelpers
{
    // =========================================================================
    // Predicates for ForAttributeWithMetadataName
    // =========================================================================

    /// <summary>
    ///     Predicate that accepts class declarations.
    /// </summary>
    public static bool IsClass(SyntaxNode node, CancellationToken _)
    {
        return node is ClassDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts record declarations.
    /// </summary>
    public static bool IsRecord(SyntaxNode node, CancellationToken _)
    {
        return node is RecordDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts struct declarations.
    /// </summary>
    public static bool IsStruct(SyntaxNode node, CancellationToken _)
    {
        return node is StructDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts any type declaration (class, struct, record, interface).
    /// </summary>
    public static bool IsTypeDeclaration(SyntaxNode node, CancellationToken _)
    {
        return node is TypeDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts class or record declarations.
    /// </summary>
    public static bool IsClassOrRecord(SyntaxNode node, CancellationToken _)
    {
        return node is ClassDeclarationSyntax or RecordDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts method declarations.
    /// </summary>
    public static bool IsMethod(SyntaxNode node, CancellationToken _)
    {
        return node is MethodDeclarationSyntax;
    }

    /// <summary>
    ///     Predicate that accepts property declarations.
    /// </summary>
    public static bool IsProperty(SyntaxNode node, CancellationToken _)
    {
        return node is PropertyDeclarationSyntax;
    }

    // =========================================================================
    // Transform Helpers
    // =========================================================================

    /// <summary>
    ///     Transforms GeneratorAttributeSyntaxContext to the target symbol.
    ///     Returns null if the symbol is not a named type.
    /// </summary>
    public static INamedTypeSymbol? GetNamedTypeSymbol(
        GeneratorAttributeSyntaxContext context,
        CancellationToken _)
    {
        return context.TargetSymbol as INamedTypeSymbol;
    }

    /// <summary>
    ///     Transforms GeneratorAttributeSyntaxContext to the target method symbol.
    ///     Returns null if the symbol is not a method.
    /// </summary>
    public static IMethodSymbol? GetMethodSymbol(
        GeneratorAttributeSyntaxContext context,
        CancellationToken _)
    {
        return context.TargetSymbol as IMethodSymbol;
    }

    // =========================================================================
    // File Name Helpers
    // =========================================================================

    /// <summary>
    ///     Creates a hint name for generated source from a string.
    ///     Format: "name.g.cs"
    /// </summary>
    public static string GetHintName(string name, string? suffix = null)
    {
        var safeName = name
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_')
            .Replace(':', '_')
            .Replace('|', '_')
            .Replace('*', '_')
            .Replace('?', '_')
            .Replace('"', '_');

        if (!string.IsNullOrEmpty(suffix))
            safeName = $"{safeName}.{suffix}";

        return $"{safeName}.g.cs";
    }

    /// <summary>
    ///     Creates a hint name for generated source from a type symbol.
    ///     Format: "Namespace.TypeName.g.cs"
    /// </summary>
    public static string GetHintName(INamedTypeSymbol symbol, string? suffix = null)
    {
        var name = symbol.ToDisplayString(new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
                genericsOptions: SymbolDisplayGenericsOptions.None))
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_');

        if (!string.IsNullOrEmpty(suffix))
            name = $"{name}.{suffix}";

        return $"{name}.g.cs";
    }

    /// <summary>
    ///     Creates a hint name for generated source from a method symbol.
    ///     Format: "Namespace.TypeName.MethodName.g.cs"
    /// </summary>
    public static string GetHintName(IMethodSymbol symbol, string? suffix = null)
    {
        var typeName = symbol.ContainingType.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.None));

        var name = $"{typeName}.{symbol.Name}"
            .Replace('<', '_')
            .Replace('>', '_')
            .Replace(',', '_')
            .Replace(' ', '_');

        if (!string.IsNullOrEmpty(suffix))
            name = $"{name}.{suffix}";

        return $"{name}.g.cs";
    }

    // =========================================================================
    // Caching Helpers
    // =========================================================================

    /// <summary>
    ///     Compares two sequences for equality (useful for incremental generator caching).
    /// </summary>
    public static bool SequenceEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right)
        where T : IEquatable<T>
    {
        if (left.Length != right.Length)
            return false;

        for (var i = 0; i < left.Length; i++)
            if (!left[i].Equals(right[i]))
                return false;

        return true;
    }
}
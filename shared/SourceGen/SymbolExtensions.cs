// =============================================================================
// Pragmatic.Design - Symbol Extensions
// Helper extensions for working with Roslyn symbols in Source Generators
// =============================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGen;

/// <summary>
///     Extension methods for ISymbol and related types.
/// </summary>
internal static class SymbolExtensions
{
    // =========================================================================
    // Namespace & Type Names
    // =========================================================================

    // No GetFullyQualifiedName: ToRenderName covers the rendering case (it also maps primitives to C#
    // keywords) and GetFullName the display case.

    /// <summary>
    ///     Gets the fully qualified name without "global::" prefix.
    /// </summary>
    public static string GetFullName(this ITypeSymbol symbol)
    {
        return symbol.ToDisplayString(new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters));
    }

    /// <summary>
    ///     Gets the namespace of a symbol (e.g., "MyNamespace.SubNamespace").
    /// </summary>
    public static string? GetNamespace(this ISymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        if (ns == null || ns.IsGlobalNamespace)
            return null;

        return ns.ToDisplayString();
    }

    /// <summary>
    ///     Gets the namespace or empty string if global.
    /// </summary>
    public static string GetNamespaceOrEmpty(this ISymbol symbol)
    {
        return symbol.GetNamespace() ?? string.Empty;
    }

    // =========================================================================
    // Attribute Helpers
    // =========================================================================

    /// <summary>
    ///     Checks if a symbol has an attribute with the specified full name.
    /// </summary>
    public static bool HasAttribute(this ISymbol symbol, string attributeFullName)
    {
        return symbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == attributeFullName);
    }

    /// <summary>
    ///     Gets attribute data for an attribute with the specified full name.
    /// </summary>
    public static AttributeData? GetAttribute(this ISymbol symbol, string attributeFullName)
    {
        return symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == attributeFullName);
    }

    /// <summary>
    ///     Gets all attributes with the specified full name.
    /// </summary>
    public static IEnumerable<AttributeData> GetAttributes(this ISymbol symbol, string attributeFullName)
    {
        return symbol.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == attributeFullName);
    }

    // No GetConstructorArgument<T>: transforms read constructor arguments straight off AttributeData,
    // where they can also pattern-match the TypedConstant kind.

    /// <summary>
    ///     Gets a named argument value.
    /// </summary>
    public static T? GetNamedArgument<T>(this AttributeData attribute, string name)
    {
        var arg = attribute.NamedArguments.FirstOrDefault(a => a.Key == name);
        if (arg.Value.Value is T value)
            return value;

        return default;
    }

    /// <summary>
    ///     Gets a named <see langword="int" /> argument the caller actually wrote, or
    ///     <paramref name="defaultValue" /> when the attribute is absent or the argument was not
    ///     written.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Use this instead of <c>attribute?.GetNamedArgument&lt;int&gt;(name) ?? fallback</c>, which
    ///     reads as the same thing and is not: <see cref="GetNamedArgument{T}" /> yields 0 for an
    ///     argument nobody wrote, and 0 is not null, so the fallback fires only when the whole
    ///     attribute is missing. A bare attribute then configures zeros — well formed, and silent.
    ///     An explicit 0 still survives, because some features diagnose it.
    /// </remarks>
    public static int GetWrittenIntOrDefault(this AttributeData? attribute, string name, int defaultValue)
    {
        if (attribute is null)
            return defaultValue;

        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Value is int value)
                return value;
        }

        return defaultValue;
    }

    // =========================================================================
    // Type Checks
    // =========================================================================

    /// <summary>
    ///     Checks if a type symbol is a partial type.
    /// </summary>
    public static bool IsPartial(this INamedTypeSymbol symbol)
    {
        return symbol.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is TypeDeclarationSyntax tds &&
                      tds.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));
    }

    /// <summary>
    ///     Checks if a type implements an interface with the specified full name.
    /// </summary>
    public static bool ImplementsInterface(this ITypeSymbol symbol, string interfaceFullName)
    {
        return symbol.AllInterfaces.Any(i => i.ToDisplayString() == interfaceFullName);
    }

    /// <summary>
    ///     Checks if a type inherits from a base type with the specified full name.
    /// </summary>
    public static bool InheritsFrom(this ITypeSymbol symbol, string baseTypeFullName)
    {
        var current = symbol.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == baseTypeFullName)
                return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    ///     Gets the type kind as a C# keyword (class, struct, record, interface).
    /// </summary>
    public static string GetTypeKindKeyword(this INamedTypeSymbol symbol)
    {
        return symbol.TypeKind switch
        {
            TypeKind.Interface => "interface",
            TypeKind.Struct => symbol.IsRecord ? "record struct" : "struct",
            TypeKind.Class => symbol.IsRecord ? "record" : "class",
            _ => "class"
        };
    }

    // =========================================================================
    // Member Access
    // =========================================================================

    /// <summary>
    ///     Gets all public properties of a type (including inherited).
    /// </summary>
    public static IEnumerable<IPropertySymbol> GetPublicProperties(this ITypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false });
    }

    /// <summary>
    ///     Gets all public properties of a type including base types.
    /// </summary>
    public static IEnumerable<IPropertySymbol> GetAllPublicProperties(this ITypeSymbol symbol)
    {
        var current = symbol;
        while (current != null)
        {
            foreach (var prop in current.GetPublicProperties())
                yield return prop;
            current = current.BaseType;
        }
    }

    /// <summary>
    ///     Gets all public methods of a type (excluding property accessors, constructors).
    /// </summary>
    public static IEnumerable<IMethodSymbol> GetPublicMethods(this ITypeSymbol symbol)
    {
        return symbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                        m is { IsStatic: false, MethodKind: MethodKind.Ordinary });
    }

    // =========================================================================
    // Accessibility
    // =========================================================================

    /// <summary>
    ///     Gets the C# keyword for accessibility.
    /// </summary>
    public static string GetAccessibilityKeyword(this ISymbol symbol)
    {
        return symbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => "private"
        };
    }

    // =========================================================================
    // Nullability
    // =========================================================================

    /// <summary>
    ///     Checks if a type is nullable (reference type with ? or Nullable&lt;T&gt;).
    /// </summary>
    public static bool IsNullable(this ITypeSymbol symbol)
    {
        if (symbol.NullableAnnotation == NullableAnnotation.Annotated)
            return true;

        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            return true;

        return false;
    }

    /// <summary>
    ///     Gets the underlying type if nullable, otherwise returns the type itself.
    /// </summary>
    public static ITypeSymbol GetUnderlyingType(this ITypeSymbol symbol)
    {
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            return named.TypeArguments[0];

        return symbol;
    }

    // =========================================================================
    // Code Generation Names (for source generator output)
    // =========================================================================

    /// <summary>
    ///     Gets the type name suitable for code generation.
    ///     - Primitives: returns C# keyword (int, string, bool, etc.)
    ///     - Complex types: returns global::Namespace.Type
    ///     - Handles nullability with ?
    /// </summary>
    /// <remarks>
    ///     Use this when generating code that references a type to avoid
    ///     namespace collisions (e.g., Pragmatic.Ensure namespace vs Ensure class).
    /// </remarks>
    public static string ToRenderName(this ITypeSymbol symbol)
    {
        // Handle arrays specially
        if (symbol is IArrayTypeSymbol arrayType)
            return $"{arrayType.ElementType.ToRenderName()}[]";

        // Handle Nullable<T> specially
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            return $"{named.TypeArguments[0].ToRenderName()}?";

        // Check for C# built-in type aliases (primitives)
        var primitiveKeyword = GetPrimitiveKeyword(symbol);
        if (primitiveKeyword != null)
        {
            var result = primitiveKeyword;
            if (symbol.NullableAnnotation == NullableAnnotation.Annotated && !result.EndsWith("?"))
                result += "?";
            return result;
        }

        // Handle generic types
        if (symbol is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var typeArgs = string.Join(", ", genericType.TypeArguments.Select(t => t.ToRenderName()));
            var ns = genericType.ContainingNamespace?.ToDisplayString();
            var nsPrefix = string.IsNullOrEmpty(ns) ? "global::" : $"global::{ns}.";
            var baseName = genericType.ContainingType != null
                ? $"{nsPrefix}{genericType.ContainingType.Name}.{genericType.Name}"
                : $"{nsPrefix}{genericType.Name}";
            var fullName = $"{baseName}<{typeArgs}>";
            if (symbol.NullableAnnotation == NullableAnnotation.Annotated)
                fullName += "?";
            return fullName;
        }

        // Standard complex type: global::Namespace.Type
        var globalName = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (symbol.NullableAnnotation == NullableAnnotation.Annotated && !globalName.EndsWith("?"))
            globalName += "?";
        return globalName;
    }

    /// <summary>
    ///     Gets the C# keyword for primitive/built-in types, or null if not a primitive.
    /// </summary>
    private static string? GetPrimitiveKeyword(ITypeSymbol symbol)
    {
        // Strip nullable annotation for checking
        var typeToCheck = symbol;
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            typeToCheck = named.TypeArguments[0];

        return typeToCheck.SpecialType switch
        {
            SpecialType.System_Boolean => "bool",
            SpecialType.System_Byte => "byte",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Int16 => "short",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_Int32 => "int",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_Int64 => "long",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Char => "char",
            SpecialType.System_String => "string",
            SpecialType.System_Object => "object",
            SpecialType.System_Void => "void",
            _ => null
        };
    }

    /// <summary>
    ///     Gets a simple type name without namespace for display purposes.
    /// </summary>
    public static string ToSimpleName(this ITypeSymbol symbol)
    {
        // Handle arrays
        if (symbol is IArrayTypeSymbol arrayType)
            return $"{arrayType.ElementType.ToSimpleName()}[]";

        // Handle Nullable<T>
        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } named)
            return $"{named.TypeArguments[0].ToSimpleName()}?";

        // Check for primitive keyword
        var primitiveKeyword = GetPrimitiveKeyword(symbol);
        if (primitiveKeyword != null)
            return primitiveKeyword;

        // Generic types
        if (symbol is INamedTypeSymbol { IsGenericType: true } genericType)
        {
            var typeArgs = string.Join(", ", genericType.TypeArguments.Select(t => t.ToSimpleName()));
            return $"{genericType.Name}<{typeArgs}>";
        }

        return symbol.Name;
    }
}
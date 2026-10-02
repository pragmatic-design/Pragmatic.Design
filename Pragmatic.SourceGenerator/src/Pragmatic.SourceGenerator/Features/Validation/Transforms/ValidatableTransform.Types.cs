using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Type helper methods for ValidatableTransform.
/// </summary>
internal static partial class ValidatableTransform
{
    public static bool IsPartialType(TypeDeclarationSyntax typeDecl)
        => typeDecl.Modifiers.Any(SyntaxKind.PartialKeyword);

    public static string GetTypeKind(INamedTypeSymbol symbol)
    {
        if (symbol is { IsRecord: true, IsValueType: true })
            return "record struct";
        if (symbol.IsRecord)
            return "record";
        if (symbol.IsValueType)
            return "struct";
        return "class";
    }

    internal static bool IsNumericType(ITypeSymbol type)
    {
        // Unwrap nullable
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        return type.SpecialType switch
        {
            SpecialType.System_Byte => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            _ => false
        };
    }

    internal static bool IsComparable(ITypeSymbol type)
        => type.AllInterfaces.Any(i =>
            i.OriginalDefinition.ToDisplayString() == "System.IComparable" ||
            i.OriginalDefinition.ToDisplayString() == "System.IComparable<T>");

    private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        var current = type.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
            current = current.BaseType;
        }
        return false;
    }

    private static bool HasAttribute(IPropertySymbol property, INamedTypeSymbol? attributeType)
    {
        if (attributeType is null)
            return false;
        return property.GetAttributes()
            .Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, attributeType));
    }
}

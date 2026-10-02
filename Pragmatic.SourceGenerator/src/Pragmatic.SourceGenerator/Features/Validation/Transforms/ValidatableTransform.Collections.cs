using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Validation.Transforms;

/// <summary>
///     Collection analysis methods for ValidatableTransform.
/// </summary>
internal static partial class ValidatableTransform
{
    internal static (bool isCollection, string? elementType, bool elementIsValidatable) AnalyzeCollectionType(
        ITypeSymbol type,
        INamedTypeSymbol? iSyncValidator)
    {
        // Check for array
        if (type is IArrayTypeSymbol arrayType)
        {
            var elemType = arrayType.ElementType;
            var isValidatable = iSyncValidator is not null && ImplementsInterface(elemType, iSyncValidator);
            return (true, elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), isValidatable);
        }

        // Check for generic collections
        if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var typeName = namedType.OriginalDefinition.ToDisplayString();
            var isCollectionInterface =
                typeName.StartsWith("System.Collections.Generic.IEnumerable<") ||
                typeName.StartsWith("System.Collections.Generic.ICollection<") ||
                typeName.StartsWith("System.Collections.Generic.IList<") ||
                typeName.StartsWith("System.Collections.Generic.IReadOnlyCollection<") ||
                typeName.StartsWith("System.Collections.Generic.IReadOnlyList<") ||
                typeName.StartsWith("System.Collections.Generic.List<") ||
                typeName.StartsWith("System.Collections.Generic.HashSet<");

            if (isCollectionInterface && namedType.TypeArguments.Length > 0)
            {
                var elemType = namedType.TypeArguments[0];
                var isValidatable = iSyncValidator is not null && ImplementsInterface(elemType, iSyncValidator);
                return (true, elemType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), isValidatable);
            }
        }

        return (false, null, false);
    }

    private static bool AnalyzeObjectValidatable(ITypeSymbol type, INamedTypeSymbol? iSyncValidator)
    {
        if (iSyncValidator is null)
            return false;
        if (type.SpecialType != SpecialType.None)
            return false;
        if (type is not INamedTypeSymbol namedType)
            return false;
        if (namedType.IsGenericType)
            return false;
        return ImplementsInterface(type, iSyncValidator);
    }

    private static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol interfaceType)
    {
        if (type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, interfaceType)))
            return true;

        // Heuristic: types that will implement ISyncValidator via source generation
        if (type is INamedTypeSymbol namedType)
            foreach (var member in namedType.GetMembers())
                if (member is IPropertySymbol prop)
                    foreach (var attr in prop.GetAttributes())
                        if (attr.AttributeClass is not null &&
                            KnownValidationAttributes.Contains(attr.AttributeClass.ToDisplayString()))
                            return true;

        return false;
    }
}

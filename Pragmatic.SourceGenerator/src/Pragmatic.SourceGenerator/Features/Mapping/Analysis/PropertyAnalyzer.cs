using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Mapping.Analysis;

/// <summary>
///     Helper methods for property analysis.
/// </summary>
internal static class PropertyAnalyzer
{
    /// <summary>
    ///     Gets all public instance properties from a type and its base types.
    /// </summary>
    public static ImmutableArray<IPropertySymbol> GetAllProperties(INamedTypeSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<IPropertySymbol>();

        var current = symbol;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
                if (member is IPropertySymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } prop)
                    // Avoid duplicates from inheritance
                    if (!builder.Any(p => p.Name == prop.Name))
                        builder.Add(prop);

            current = current.BaseType;
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Checks if a property has an init-only setter.
    /// </summary>
    public static bool IsInitOnly(IPropertySymbol prop)
    {
        return prop.SetMethod?.IsInitOnly == true;
    }

    /// <summary>
    ///     Checks if a property has a private setter.
    /// </summary>
    public static bool HasPrivateSetter(IPropertySymbol prop)
    {
        return prop.SetMethod?.DeclaredAccessibility == Accessibility.Private;
    }

    /// <summary>
    ///     Checks if a property name represents an ID property.
    /// </summary>
    public static bool IsIdProperty(string propertyName, string entityName)
    {
        return propertyName.Equals("Id", StringComparison.OrdinalIgnoreCase)
               || propertyName.Equals($"{entityName}Id", StringComparison.OrdinalIgnoreCase);
    }
}

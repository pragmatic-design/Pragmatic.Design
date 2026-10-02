using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Analyzes DTOs with [MapFrom] attributes to infer required includes and projections.
/// </summary>
internal static class MappingInferenceAnalyzer
{
    private const string MapFromAttributeName = "Pragmatic.Mapping.MapFromAttribute`1";

    /// <summary>
    ///     Analyzes a DTO type to infer mapping information.
    /// </summary>
    public static MappingInferenceModel? Analyze(
        INamedTypeSymbol dtoType,
        Compilation compilation)
    {
        // Find [MapFrom<TEntity>] attribute
        var mapFromAttr = dtoType.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() is { } name &&
                                 name.StartsWith("Pragmatic.Mapping.MapFromAttribute", StringComparison.Ordinal));

        if (mapFromAttr?.AttributeClass is not INamedTypeSymbol attrClass ||
            attrClass.TypeArguments.Length != 1 ||
            attrClass.TypeArguments[0] is not INamedTypeSymbol entityType)
            return null;

        var includePaths = new List<IncludePathModel>();
        var propertyMappings = new List<PropertyMappingModel>();

        // Analyze each property in the DTO
        foreach (var property in dtoType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;

            var mapping = AnalyzeProperty(property, entityType, compilation, [], includePaths);
            if (mapping != null)
                propertyMappings.Add(mapping);
        }

        return new MappingInferenceModel
        {
            EntityTypeFullName = entityType.ToDisplayString(),
            DtoTypeFullName = dtoType.ToDisplayString(),
            DtoTypeName = dtoType.Name,
            Namespace = dtoType.ContainingNamespace?.ToDisplayString() ?? "",
            IncludePaths = includePaths,
            PropertyMappings = propertyMappings
        };
    }

    private static PropertyMappingModel? AnalyzeProperty(
        IPropertySymbol dtoProperty,
        INamedTypeSymbol entityType,
        Compilation compilation,
        List<string> currentPath,
        List<IncludePathModel> includePaths)
    {
        var propertyName = dtoProperty.Name;
        var propertyType = dtoProperty.Type;

        // Check if entity has a matching property
        var entityProperty = FindEntityProperty(entityType, propertyName);

        if (entityProperty == null)
        {
            // Property doesn't exist on entity - might be computed or custom mapped
            return new PropertyMappingModel
            {
                DtoPropertyName = propertyName,
                EntityPropertyPath = propertyName, // Assume same name
                DtoPropertyType = propertyType.ToDisplayString()
            };
        }

        var entityPath = currentPath.Count > 0
            ? string.Join(".", currentPath) + "." + propertyName
            : propertyName;

        // Check if this is a navigation property (reference to another entity)
        if (IsNavigationProperty(entityProperty, out var targetEntityType, out var isCollection))
        {
            // Check if the DTO property type has [MapFrom] (nested DTO)
            var dtoPropertyType = GetUnderlyingType(propertyType);

            if (dtoPropertyType is INamedTypeSymbol namedDtoType && HasMapFromAttribute(namedDtoType))
            {
                // This is a nested DTO - add include path
                var pathSegments = new List<string>(currentPath) { propertyName };
                includePaths.Add(new IncludePathModel
                {
                    PathSegments = pathSegments,
                    IsCollection = isCollection,
                    TargetEntityType = targetEntityType?.ToDisplayString() ?? ""
                });

                // Recursively analyze nested DTO for ThenInclude
                if (targetEntityType != null)
                {
                    AnalyzeNestedIncludes(namedDtoType, targetEntityType, compilation, pathSegments, includePaths, visited: null);
                }

                return new PropertyMappingModel
                {
                    DtoPropertyName = propertyName,
                    EntityPropertyPath = entityPath,
                    DtoPropertyType = propertyType.ToDisplayString(),
                    IsNestedDto = true,
                    NestedDtoType = dtoPropertyType.ToDisplayString(),
                    IsCollection = isCollection,
                    CollectionElementType = isCollection ? dtoPropertyType.ToDisplayString() : null
                };
            }
        }

        // Simple property mapping
        return new PropertyMappingModel
        {
            DtoPropertyName = propertyName,
            EntityPropertyPath = entityPath,
            DtoPropertyType = propertyType.ToDisplayString()
        };
    }

    private static void AnalyzeNestedIncludes(
        INamedTypeSymbol nestedDtoType,
        INamedTypeSymbol entityType,
        Compilation compilation,
        List<string> currentPath,
        List<IncludePathModel> includePaths,
        HashSet<string>? visited = null)
    {
        // Guard against circular entity relationships to prevent StackOverflow.
        visited ??= new HashSet<string>(StringComparer.Ordinal);
        var dtoKey = nestedDtoType.ToDisplayString();
        if (!visited.Add(dtoKey))
            return;

        foreach (var property in nestedDtoType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;

            var entityProperty = FindEntityProperty(entityType, property.Name);
            if (entityProperty == null)
                continue;

            if (IsNavigationProperty(entityProperty, out var targetEntityType, out var isCollection))
            {
                var dtoPropertyType = GetUnderlyingType(property.Type);

                if (dtoPropertyType is INamedTypeSymbol namedDtoType && HasMapFromAttribute(namedDtoType))
                {
                    var pathSegments = new List<string>(currentPath) { property.Name };
                    includePaths.Add(new IncludePathModel
                    {
                        PathSegments = pathSegments,
                        IsCollection = isCollection,
                        TargetEntityType = targetEntityType?.ToDisplayString() ?? ""
                    });

                    // Continue recursion for deeper nesting (pass visited set to prevent StackOverflow)
                    if (targetEntityType != null)
                    {
                        AnalyzeNestedIncludes(namedDtoType, targetEntityType, compilation, pathSegments, includePaths, visited);
                    }
                }
            }
        }
    }

    private static IPropertySymbol? FindEntityProperty(INamedTypeSymbol entityType, string propertyName)
    {
        var current = entityType;
        while (current != null)
        {
            var property = current.GetMembers(propertyName).OfType<IPropertySymbol>().FirstOrDefault();
            if (property != null)
                return property;
            current = current.BaseType;
        }
        return null;
    }

    private static bool IsNavigationProperty(
        IPropertySymbol property,
        out INamedTypeSymbol? targetType,
        out bool isCollection)
    {
        targetType = null;
        isCollection = false;

        var propertyType = property.Type;

        // Check for collection types (ICollection<T>, IList<T>, List<T>, etc.)
        if (propertyType is INamedTypeSymbol namedType)
        {
            if (IsCollectionType(namedType, out var elementType))
            {
                if (elementType is INamedTypeSymbol namedElementType && IsEntityType(namedElementType))
                {
                    targetType = namedElementType;
                    isCollection = true;
                    return true;
                }
            }

            // Check for reference navigation (single entity)
            if (IsEntityType(namedType))
            {
                targetType = namedType;
                isCollection = false;
                return true;
            }
        }

        return false;
    }

    private static bool IsCollectionType(INamedTypeSymbol type, out ITypeSymbol? elementType)
    {
        elementType = null;

        // Check if it's a generic collection
        if (type is { IsGenericType: true, TypeArguments.Length: 1 })
        {
            var typeName = type.OriginalDefinition.ToDisplayString();
            if (typeName.StartsWith("System.Collections.Generic.ICollection", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Collections.Generic.IList", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Collections.Generic.List", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Collections.Generic.IEnumerable", StringComparison.Ordinal) ||
                typeName.StartsWith("System.Collections.Generic.HashSet", StringComparison.Ordinal))
            {
                elementType = type.TypeArguments[0];
                return true;
            }
        }

        // Check interfaces
        foreach (var iface in type.AllInterfaces)
        {
            if (IsCollectionType(iface, out elementType))
                return true;
        }

        return false;
    }

    private static bool IsEntityType(INamedTypeSymbol type)
    {
        // Check if type has [Entity] or [EntityAttribute] (exact name, not StartsWith)
        var hasEntityAttribute = type.GetAttributes()
            .Any(a => a.AttributeClass?.Name is "EntityAttribute"
                    && a.AttributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity");

        if (hasEntityAttribute)
            return true;

        // Heuristic: class with Id or PersistenceId property is likely an entity
        if (type is { TypeKind: TypeKind.Class, IsAbstract: false })
        {
            var hasIdProperty = type.GetMembers()
                .OfType<IPropertySymbol>()
                .Any(p => p.Name is "Id" or "PersistenceId");
            return hasIdProperty;
        }

        return false;
    }

    private static bool HasMapFromAttribute(INamedTypeSymbol type)
    {
        return type.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() is { } name &&
                      name.StartsWith("Pragmatic.Mapping.MapFromAttribute", StringComparison.Ordinal));
    }

    private static ITypeSymbol GetUnderlyingType(ITypeSymbol type)
    {
        // For collections, get the element type
        if (type is INamedTypeSymbol namedType && IsCollectionType(namedType, out var elementType) && elementType != null)
            return elementType;

        return type;
    }
}

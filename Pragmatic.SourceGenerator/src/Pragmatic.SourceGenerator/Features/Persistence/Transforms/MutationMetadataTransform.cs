using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform for extracting mutation metadata from DTOs with [Mutation&lt;TEntity&gt;].
/// </summary>
internal static class MutationMetadataTransform
{
    /// <summary>
    ///     The fully qualified name of the Patch attribute.
    /// </summary>
    public const string PatchAttributeName = "Pragmatic.Persistence.Patch.PatchAttribute`1";

    /// <summary>
    ///     Transforms a syntax node into a MutationMetadataModel.
    /// </summary>
    public static MutationMetadataModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        if (context.TargetNode is not TypeDeclarationSyntax typeDecl)
            return null;

        var symbol = context.TargetSymbol as INamedTypeSymbol;
        if (symbol is null)
            return null;

        // Get the [Mutation<TEntity>] attribute
        var mutationAttr = context.Attributes.FirstOrDefault();
        if (mutationAttr is null)
            return null;

        // Extract entity type from generic argument
        var entityType = mutationAttr.AttributeClass?.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        if (entityType is null)
            return null;

        // Get entity ID type from [Entity] on the entity
        var entityIdType = GetEntityIdType(entityType);

        // Check if DTO has an ID property
        var idPropertyOnDto = FindIdPropertyOnDto(symbol, entityType);

        // Collect required includes from navigation properties
        var requiredIncludes = CollectRequiredIncludes(symbol, entityType);

        // Extract property mappings (reads [MapIgnore], [MapProperty], [MapConverter])
        var properties = MutationPropertyTransform.ExtractProperties(symbol, entityType);

        // Check if DTO has [MapTo<TEntity>] - if so, we can delegate to its ApplyTo() method
        var hasMapTo = HasMapToAttribute(symbol, entityType);

        // Detect lifecycle hooks (nested classes: PreLoad, AfterLoad, PreValues, PostValues)
        var hooks = DetectHooks(symbol);

        return new MutationMetadataModel
        {
            Namespace = symbol.ContainingNamespace?.ToDisplayString() ?? "",
            TypeName = symbol.Name,
            Accessibility = symbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            EntityFullTypeName = entityType.ToDisplayString(),
            EntityTypeName = entityType.Name,
            EntityIdType = entityIdType,
            IdPropertyOnDto = idPropertyOnDto,
            RequiredIncludes = requiredIncludes,
            Properties = properties,
            HasMapToAttribute = hasMapTo,
            Hooks = hooks,
            LocationInfo = LocationInfo.From(typeDecl.GetLocation())
        };
    }

    private static string GetEntityIdType(INamedTypeSymbol entityType)
    {
        // Look for [Entity] attribute
        foreach (var attr in entityType.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            // Check for Entity<T> generic attribute
            if (attrClass.IsGenericType &&
                attrClass.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Persistence.Entity.EntityAttribute"))
            {
                var idType = attrClass.TypeArguments.FirstOrDefault();
                if (idType is not null)
                    return idType.ToDisplayString();
            }
        }

        return "System.Guid"; // Default
    }

    private static string? FindIdPropertyOnDto(INamedTypeSymbol dtoType, INamedTypeSymbol entityType)
    {
        // Look for properties named Id, {Entity}Id, or PersistenceId
        var possibleNames = new[]
        {
            "Id",
            $"{entityType.Name}Id",
            "PersistenceId"
        };

        foreach (var member in dtoType.GetMembers())
            if (member is IPropertySymbol prop && possibleNames.Contains(prop.Name))
                return prop.Name;

        return null;
    }

    private static ImmutableArray<string> CollectRequiredIncludes(
        INamedTypeSymbol dtoType,
        INamedTypeSymbol entityType)
    {
        var includes = ImmutableArray.CreateBuilder<string>();

        // Get entity navigation properties
        var entityNavigations = entityType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => IsNavigationProperty(p))
            .ToDictionary(p => p.Name, p => p);

        // Check DTO properties - if they reference a nested Mutation DTO,
        // we need to Include the corresponding navigation
        foreach (var dtoProp in dtoType.GetMembers().OfType<IPropertySymbol>())
            // Check if entity has a matching navigation property
            if (entityNavigations.TryGetValue(dtoProp.Name, out var entityNav))
            {
                // ⚠️ The name says Mutation and the check reads Patch, and there is a second copy of
                // this same mismatch in MappingTransform. A nested MUTATION is not detected here or
                // anywhere: MutationChildAnalyzer accepts a child only for [MapTo] or [Patch].
                var dtoPropType = GetUnderlyingType(dtoProp.Type);
                if (HasPatchAttribute(dtoPropType))
                    includes.Add(dtoProp.Name);
            }

        return includes.ToImmutable();
    }

    private static bool IsNavigationProperty(IPropertySymbol prop)
    {
        // Navigation properties are reference types or collections
        var type = prop.Type;

        // Skip value types and strings
        if (type.IsValueType || type.SpecialType == SpecialType.System_String)
            return false;

        // Check if it's a collection
        if (IsCollectionType(type))
            return true;

        // Check if it's a class type (potential navigation)
        return type.TypeKind == TypeKind.Class;
    }

    private static bool IsCollectionType(ITypeSymbol type)
    {
        // Check for ICollection<T>, IList<T>, List<T>, etc.
        if (type is INamedTypeSymbol namedType)
        {
            var interfaces = namedType.AllInterfaces;
            return interfaces.Any(i =>
                i.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.ICollection") ||
                i.OriginalDefinition.ToDisplayString().StartsWith("System.Collections.Generic.IEnumerable"));
        }

        return false;
    }

    private static ITypeSymbol GetUnderlyingType(ITypeSymbol type)
    {
        // Unwrap nullable
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } namedType)
            return namedType.TypeArguments[0];

        // Unwrap collection
        if (IsCollectionType(type) && type is INamedTypeSymbol collectionType)
        {
            var elementType = collectionType.TypeArguments.FirstOrDefault();
            if (elementType is not null)
                return elementType;
        }

        return type;
    }

    private static bool HasPatchAttribute(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
            return false;

        return namedType.GetAttributes().Any(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString()
                .StartsWith("Pragmatic.Persistence.Patch.PatchAttribute") == true);
    }


    /// <summary>
    ///     Detects lifecycle hook nested classes inside the mutation DTO.
    ///     Valid hook names: PreLoad, AfterLoad, PreValues, PostValues.
    ///     Each must have a static Execute() method.
    /// </summary>
    private static ImmutableArray<MutationHookModel> DetectHooks(INamedTypeSymbol dtoType)
    {
        var hookNames = new Dictionary<string, MutationHookType>
        {
            ["PreLoad"] = MutationHookType.PreLoad,
            ["AfterLoad"] = MutationHookType.AfterLoad,
            ["PreValues"] = MutationHookType.PreValues,
            ["PostValues"] = MutationHookType.PostValues
        };

        var hooks = ImmutableArray.CreateBuilder<MutationHookModel>();

        foreach (var nestedType in dtoType.GetTypeMembers())
        {
            if (!hookNames.TryGetValue(nestedType.Name, out var hookType))
                continue;

            // Validate: must have a static Execute() method
            var executeMethod = nestedType.GetMembers("Execute")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public);

            if (executeMethod is null)
                continue;

            // Check if async (returns Task or Task<T>)
            var isAsync = executeMethod.ReturnType.ToDisplayString().StartsWith("System.Threading.Tasks.Task");

            hooks.Add(new MutationHookModel
            {
                HookType = hookType,
                ClassName = nestedType.Name,
                IsAsync = isAsync
            });
        }

        return hooks.ToImmutable();
    }

    /// <summary>
    ///     Checks if a type has the [MapTo&lt;T&gt;] attribute from Pragmatic.Mapping.
    /// </summary>
    private static bool HasMapToAttribute(INamedTypeSymbol type, INamedTypeSymbol entityType)
    {
        return type.GetAttributes().Any(a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
                return false;

            // Check if it's MapToAttribute<T>
            if (!attrClass.OriginalDefinition.ToDisplayString()
                    .StartsWith("Pragmatic.Mapping.MapToAttribute"))
                return false;

            // Check if generic argument matches entity type
            if (attrClass.TypeArguments.Length != 1)
                return false;

            return SymbolEqualityComparer.Default.Equals(attrClass.TypeArguments[0], entityType);
        });
    }
}

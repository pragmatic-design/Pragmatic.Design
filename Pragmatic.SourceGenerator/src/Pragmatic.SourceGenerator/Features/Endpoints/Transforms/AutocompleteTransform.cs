using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Transforms a property with [Autocomplete] attribute into an AutocompleteModel.
///     Detects the containing entity's key property for the autocomplete result.
/// </summary>
internal static class AutocompleteTransform
{
    public static AutocompleteModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not IPropertySymbol propertySymbol)
            return null;

        var containingType = propertySymbol.ContainingType;
        if (containingType is null)
            return null;

        // Validate that the property is a string type
        if (propertySymbol.Type.SpecialType != SpecialType.System_String)
        {
            var ns2 = containingType.ContainingNamespace.IsGlobalNamespace
                ? ""
                : containingType.ContainingNamespace.ToDisplayString();

            return new AutocompleteModel
            {
                EntityNamespace = ns2,
                EntityTypeName = containingType.Name,
                EntityTypeFullName = containingType.ToDisplayString(),
                PropertyName = propertySymbol.Name,
                KeyPropertyTypeName = "",
                KeyPropertyName = "",
                Route = "",
                DefaultLimit = 10,
                Accessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant(),
                InvalidReason = AutocompleteInvalidReason.NotStringProperty,
                LocationInfo = LocationInfo.From(propertySymbol.Locations.FirstOrDefault())
            };
        }

        var ns = containingType.ContainingNamespace.IsGlobalNamespace
            ? ""
            : containingType.ContainingNamespace.ToDisplayString();

        // Parse [Autocomplete] or [Autocomplete<TDto>] attribute
        var (route, defaultLimit, dtoTypeFqn, dtoTypeName) = ParseAutocompleteAttribute(context.Attributes, containingType, propertySymbol);

        // Find the key property on the entity
        var (keyName, keyType) = FindKeyProperty(containingType);

        // Detect [BelongsTo<T>] for keyed DbContext resolution
        var boundaryType = FindBelongsToAttribute(containingType);

        if (keyName is null)
            return new AutocompleteModel
            {
                EntityNamespace = ns,
                EntityTypeName = containingType.Name,
                EntityTypeFullName = containingType.ToDisplayString(),
                PropertyName = propertySymbol.Name,
                KeyPropertyTypeName = "",
                KeyPropertyName = "",
                Route = route,
                DefaultLimit = defaultLimit,
                Accessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant(),
                BoundaryTypeFullName = boundaryType,
                DtoTypeFullName = dtoTypeFqn,
                DtoTypeName = dtoTypeName,
                InvalidReason = AutocompleteInvalidReason.MissingKeyProperty,
                LocationInfo = LocationInfo.From(propertySymbol.Locations.FirstOrDefault())
            };

        return new AutocompleteModel
        {
            EntityNamespace = ns,
            EntityTypeName = containingType.Name,
            EntityTypeFullName = containingType.ToDisplayString(),
            PropertyName = propertySymbol.Name,
            KeyPropertyTypeName = keyType!,
            KeyPropertyName = keyName,
            Route = route,
            DefaultLimit = defaultLimit,
            Accessibility = containingType.DeclaredAccessibility.ToString().ToLowerInvariant(),
            BoundaryTypeFullName = boundaryType,
            DtoTypeFullName = dtoTypeFqn,
            DtoTypeName = dtoTypeName,
            EntityReadPermission = DeriveReadPermission(boundaryType, containingType.Name),
            InvalidReason = AutocompleteInvalidReason.None,
            LocationInfo = LocationInfo.From(propertySymbol.Locations.FirstOrDefault())
        };
    }

    /// <summary>
    ///     Derives the CRUD read permission the autocomplete route enforces, from boundary type and
    ///     entity name. E.g. <c>"global::Showcase.Booking.BookingBoundary"</c> + <c>"Reservation"</c>
    ///     → <c>"booking.reservation.read"</c>.
    /// </summary>
    /// <remarks>
    ///     The value must be the one <c>EntityCrudPermissionModel</c> emits, because that is the only
    ///     permission a role can actually be granted. This derived it separately with a flat
    ///     <c>ToLowerInvariant</c>, so a two-word entity produced <c>knowledge.knowledgeitem.read</c>
    ///     against the emitted <c>knowledge.knowledge-item.read</c> — an autocomplete route gated on a
    ///     permission nobody can hold, and a 403 no grant could lift. Invisible on a single-word name,
    ///     which is how it lasted; the fifth copy of a rule that is supposed to have one home.
    /// </remarks>
    private static string? DeriveReadPermission(string? boundaryTypeFullName, string entityName)
    {
        if (boundaryTypeFullName is null)
            return PermissionNaming.ValueForEntityMember(null, entityName, "read");

        // Extract boundary class name: "global::Showcase.Booking.BookingBoundary" → "BookingBoundary"
        var boundaryName = boundaryTypeFullName;
        var lastDot = boundaryName.LastIndexOf('.');
        if (lastDot >= 0)
            boundaryName = boundaryName.Substring(lastDot + 1);

        // Flat-lowercased, matching PersistenceFeature's BoundarySlug — the boundary segment is the
        // one part the two producers already agreed on, and kebabbing it here would break that.
        const string suffix = "Boundary";
        var slug = boundaryName.EndsWith(suffix, StringComparison.Ordinal)
            ? boundaryName.Substring(0, boundaryName.Length - suffix.Length).ToLowerInvariant()
            : boundaryName.ToLowerInvariant();

        return PermissionNaming.ValueForEntityMember(slug, entityName, "read");
    }

    private static (string Route, int DefaultLimit, string? DtoTypeFullName, string? DtoTypeName) ParseAutocompleteAttribute(
        System.Collections.Immutable.ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol containingType,
        IPropertySymbol propertySymbol)
    {
        var attr = attributes.FirstOrDefault();

        string? route = null;
        var defaultLimit = 10;
        string? dtoTypeFqn = null;
        string? dtoTypeName = null;

        if (attr is not null)
        {
            foreach (var namedArg in attr.NamedArguments)
            {
                switch (namedArg.Key)
                {
                    case "Route" when namedArg.Value.Value is string r:
                        route = r;
                        break;
                    case "DefaultLimit" when namedArg.Value.Value is int limit:
                        defaultLimit = limit;
                        break;
                }
            }

            // Detect generic [Autocomplete<TDto>] — extract TDto type argument
            var attrClass = attr.AttributeClass;
            if (attrClass is { IsGenericType: true, TypeArguments.Length: 1 })
            {
                var dtoType = attrClass.TypeArguments[0];
                dtoTypeFqn = dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                dtoTypeName = dtoType.Name;
            }
        }

        // Default route: /{entities}/autocomplete/{property} (pluralized, lowercase).
        // No hardcoded "api/" prefix — the host's PragmaticEndpointsOptions.RoutePrefix
        // ("/api" in most samples) is prepended at map time, so baking it in here would
        // produce /api/api/... when a prefix is configured. Override with [Autocomplete(route: "...")]
        // if a different path is needed.
        route ??= $"/{StringHelper.Pluralize(containingType.Name).ToLowerInvariant()}/autocomplete/{propertySymbol.Name.ToLowerInvariant()}";

        return (route, defaultLimit, dtoTypeFqn, dtoTypeName);
    }

    private static (string? KeyName, string? KeyType) FindKeyProperty(INamedTypeSymbol entityType)
    {
        var current = entityType;
        while (current is not null)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                    continue;

                // Check for [Key] attribute
                foreach (var attr in prop.GetAttributes())
                {
                    var attrName = attr.AttributeClass?.ToDisplayString();
                    if (attrName == "System.ComponentModel.DataAnnotations.KeyAttribute")
                        return (prop.Name,
                            prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                // Convention: property named "Id" or "{EntityName}Id"
                if (prop.Name == "Id" || prop.Name == $"{entityType.Name}Id")
                    return (prop.Name, prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
            }

            current = current.BaseType;
        }

        // Fallback: check trait-generated properties (EntityTraitsTemplate will generate Id)
        if (TraitPropertyResolver.WillHaveIdProperty(entityType))
        {
            var traitProps = TraitPropertyResolver.GetTraitProperties(entityType);
            foreach (var vp in traitProps)
            {
                if (vp.Name == "Id")
                    return ("Id", $"global::{vp.TypeFullName}");
            }
        }

        return (null, null);
    }

    /// <summary>The entity's boundary, for the keyed DbContext the endpoint resolves.</summary>
    private static string? FindBelongsToAttribute(INamedTypeSymbol symbol)
        => Core.BoundaryOwnershipReader.QualifiedBoundaryOf(symbol);
}

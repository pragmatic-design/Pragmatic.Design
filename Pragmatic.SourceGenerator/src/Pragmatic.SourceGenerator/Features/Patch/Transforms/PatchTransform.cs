using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Features.Patch.Models;

namespace Pragmatic.SourceGenerator.Features.Patch.Transforms;

/// <summary>
///     Extracts a <see cref="PatchModel" /> from a class annotated with
///     <c>[GeneratePatch&lt;TEntity&gt;]</c>.
/// </summary>
internal static class PatchTransform
{
    // Properties excluded by name (infrastructure concerns)
    private static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.Ordinal)
    {
        "Id", "PersistenceId", "RowVersion",
        "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy",
        "DeletedAt", "DeletedBy", "IsDeleted"
    };

    // Interfaces whose members should be excluded. Tenant/owner/scope columns are isolation boundaries:
    // patching TenantId reassigns the row to another tenant, OwnerId reassigns ownership, AccessScopes
    // widens visibility — all of which a PATCH must never expose (mass-assignment). Excluded by interface
    // (not by name) so a plain FK happening to be called "OwnerId" on a non-IOwnedEntity stays patchable.
    private static readonly HashSet<string> ExcludedInterfaces = new(StringComparer.Ordinal)
    {
        "Pragmatic.Persistence.Entity.IAuditable",
        "Pragmatic.Persistence.Entity.ISoftDelete",
        "Pragmatic.MultiTenancy.ITenantEntity",
        "Pragmatic.Persistence.Entity.IOwnedEntity",
        "Pragmatic.Persistence.Entity.IScopedEntity"
    };

    public static PatchModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol patchType)
            return null;

        ct.ThrowIfCancellationRequested();

        var isPartial = context.TargetNode is TypeDeclarationSyntax tds &&
                        tds.Modifiers.Any(m => m.Text == "partial");

        if (!isPartial)
            return new PatchModel
            {
                TypeName = patchType.Name,
                Namespace = patchType.ContainingNamespace?.ToDisplayString() ?? "",
                Reason = InvalidReason.NotPartial
            };

        var attributeData = context.Attributes.FirstOrDefault();
        if (attributeData?.AttributeClass is not { IsGenericType: true } attrClass)
            return null;

        var entityType = attrClass.TypeArguments.FirstOrDefault() as INamedTypeSymbol;
        // Roslyn returns an ErrorTypeSymbol (not null) when the generic argument can't be resolved;
        // treat it as unresolvable to fire PRAG1901 instead of falling through to NoProperties (PRAG1902).
        if (entityType is null || entityType.TypeKind == TypeKind.Error)
            return new PatchModel
            {
                TypeName = patchType.Name,
                Namespace = patchType.ContainingNamespace?.ToDisplayString() ?? "",
                Reason = InvalidReason.EntityTypeNotFound
            };

        ct.ThrowIfCancellationRequested();

        var excluded = ReadPatchIgnores(patchType);
        var properties = CollectPatchProperties(entityType, excluded);

        // A name that matches nothing is a typo, and a typo here silently restores the property the
        // author wrote the attribute to protect — the opposite of what the line says.
        var unmatched = excluded
            .Where(name => !EntityHasProperty(entityType, name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToImmutableArray();

        if (properties.Length == 0)
            return new PatchModel
            {
                TypeName = patchType.Name,
                Namespace = patchType.ContainingNamespace?.ToDisplayString() ?? "",
                EntityFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                EntityName = entityType.Name,
                Reason = InvalidReason.NoProperties
            };

        return new PatchModel
        {
            Namespace = patchType.ContainingNamespace?.ToDisplayString() ?? "",
            TypeName = patchType.Name,
            Accessibility = patchType.DeclaredAccessibility.ToString().ToLowerInvariant(),
            EntityFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityName = entityType.Name,
            Properties = properties,
            UnmatchedIgnores = unmatched
        };
    }

    /// <summary>The property names <c>[PatchIgnore]</c> asks to leave out.</summary>
    private static ImmutableHashSet<string> ReadPatchIgnores(INamedTypeSymbol patchType)
    {
        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);

        foreach (var attribute in patchType.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "Pragmatic.Patch.Attributes.PatchIgnoreAttribute")
                continue;

            // params string[] arrives as one array argument; a single name arrives on its own.
            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument.Kind == TypedConstantKind.Array)
                {
                    foreach (var value in argument.Values)
                        if (value.Value is string name && name.Length > 0)
                            builder.Add(name);
                }
                else if (argument.Value is string single && single.Length > 0)
                {
                    builder.Add(single);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Whether the entity will have a property by this name once every generator has run.
    /// </summary>
    /// <remarks>
    ///     The predicted members are part of the answer, not an extra. Asking the symbol alone made
    ///     <c>[PatchIgnore("ParentId")]</c> — a relation's key, which the author cannot declare — read
    ///     as a typo: the diagnostic fired on a name that does exist, and the exclusion it asked for
    ///     was applied anyway. A check that reports the right names is the other half of applying
    ///     them.
    /// </remarks>
    private static bool EntityHasProperty(INamedTypeSymbol entityType, string name)
    {
        var current = entityType;
        while (current is not null && current.SpecialType == SpecialType.None)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().Any())
                return true;

            current = current.BaseType;
        }

        foreach (var predicted in Core.TraitPropertyResolver.GetGeneratedProperties(entityType))
        {
            if (string.Equals(predicted.Name, name, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static ImmutableArray<PatchPropertyModel> CollectPatchProperties(
        INamedTypeSymbol entityType, ImmutableHashSet<string> excluded)
    {
        var builder = ImmutableArray.CreateBuilder<PatchPropertyModel>();
        var excludedFromInterfaces = GetExcludedInterfaceMembers(entityType);

        var current = entityType;
        while (current is not null && current.SpecialType == SpecialType.None)
        {
            foreach (var member in current.GetMembers())
            {
                if (member is not IPropertySymbol prop)
                    continue;
                if (prop.IsStatic || prop.IsIndexer)
                    continue;
                if (prop.DeclaredAccessibility != Accessibility.Public)
                    continue;
                if (ExcludedPropertyNames.Contains(prop.Name))
                    continue;
                // [PatchIgnore]: the domain says this one is not correctable.
                if (excluded.Contains(prop.Name))
                    continue;
                if (excludedFromInterfaces.Contains(prop.Name))
                    continue;
                if (IsNavigationProperty(prop))
                    continue;

                var hasSetter = prop.SetMethod is not null;
                var hasSetMethod = HasSetMethodForProperty(entityType, prop.Name);

                if (!hasSetter && !hasSetMethod)
                    continue;
                if (builder.Any(p => p.Name == prop.Name))
                    continue;

                var typeDisplay = prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var isNullable = prop.Type.NullableAnnotation == NullableAnnotation.Annotated ||
                                 prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

                builder.Add(new PatchPropertyModel
                {
                    Name = prop.Name,
                    TypeFullName = typeDisplay,
                    IsNullable = isNullable,
                    HasSetMethod = hasSetMethod && (prop.SetMethod is null || !IsPublicSetter(prop)),
                    IsValueType = prop.Type.IsValueType
                });
            }

            current = current.BaseType;
        }

        AddPredictedForeignKeys(entityType, excluded, excludedFromInterfaces, builder);

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Adds the foreign keys the entity's <c>[Relation.*]</c> declarations will produce.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The loop above asks the entity for its members, and a relation's key is not one of them
    ///         during this pass: the generator that writes it has not run. So a patch over an entity
    ///         whose parent comes from a relation was emitted with a branch for every declared property
    ///         and no line for the key — silently, since an author cannot name what another generator
    ///         adds and there was nothing to report a name against.
    ///     </para>
    ///     <para>
    ///         Foreign keys only, deliberately. The predictor also offers navigations, and a navigation
    ///         is an entity reference, not a column: putting one on a patch would let a correction
    ///         replace a whole graph. Trait columns stay out too — they are already excluded by name
    ///         and by interface above, because patching <c>TenantId</c> or <c>IsDeleted</c> is not a
    ///         correction.
    ///     </para>
    ///     <para>
    ///         The setter is <c>Set{Name}</c>: the relations template emits the key with a private
    ///         setter and that <c>internal</c> method beside it, and the patch record lives in the same
    ///         assembly.
    ///     </para>
    /// </remarks>
    private static void AddPredictedForeignKeys(
        INamedTypeSymbol entityType,
        ImmutableHashSet<string> excluded,
        System.Collections.Generic.HashSet<string> excludedFromInterfaces,
        ImmutableArray<PatchPropertyModel>.Builder builder)
    {
        foreach (var fk in Core.TraitPropertyResolver.GetRelationForeignKeys(entityType))
        {
            if (ExcludedPropertyNames.Contains(fk.Name))
                continue;
            if (excluded.Contains(fk.Name))
                continue;
            if (excludedFromInterfaces.Contains(fk.Name))
                continue;
            if (builder.Any(p => p.Name == fk.Name))
                continue;

            var isNullable = fk.TypeFullName.EndsWith("?", StringComparison.Ordinal);

            builder.Add(new PatchPropertyModel
            {
                Name = fk.Name,
                TypeFullName = fk.TypeFullName,
                IsNullable = isNullable,
                HasSetMethod = true,
                // A key is the target entity's id type — Guid, int, or a string id. The suffix the
                // template appends exists to silence a null warning on a non-nullable reference, so
                // it is wrong to claim "value type" for a string key.
                IsValueType = !fk.TypeFullName.StartsWith("string", StringComparison.Ordinal)
                              && !fk.TypeFullName.StartsWith("global::System.String", StringComparison.Ordinal)
            });
        }
    }

    private static bool IsPublicSetter(IPropertySymbol prop)
        => prop.SetMethod is { DeclaredAccessibility: Accessibility.Public };

    private static bool HasSetMethodForProperty(INamedTypeSymbol type, string propertyName)
    {
        var methodName = $"Set{propertyName}";
        var current = type;
        while (current is not null && current.SpecialType == SpecialType.None)
        {
            // Check if SetX() already exists (e.g., manually declared)
            foreach (var member in current.GetMembers(methodName))
            {
                if (member is IMethodSymbol { Parameters.Length: 1 })
                    return true;
            }

            // Check if the property has a private setter — EntitySettersTemplate
            // will generate SetX() for it (not yet visible in this SG pass)
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol prop &&
                    prop.SetMethod is { DeclaredAccessibility: not Accessibility.Public })
                    return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    private static bool IsNavigationProperty(IPropertySymbol prop)
    {
        var type = prop.Type;

        // Value types and well-known types (string, decimal, System.*) are never navigation
        if (type.IsValueType)
            return false;
        if (type is INamedTypeSymbol wellKnown && IsWellKnownType(wellKnown))
            return false;

        // Check if type is a collection (ICollection<T>, IList<T>, List<T>, etc.)
        if (type is INamedTypeSymbol namedType)
        {
            if (IsCollectionInterface(namedType))
                return true;

            foreach (var iface in namedType.AllInterfaces)
            {
                if (IsCollectionInterface(iface))
                    return true;
            }

            // Reference types with an Id/PersistenceId property are likely navigation properties
            if (namedType.SpecialType == SpecialType.None)
            {
                var hasIdProperty = namedType.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Any(p => p.Name is "Id" or "PersistenceId");

                if (hasIdProperty)
                    return true;
            }
        }

        return false;
    }

    private static bool IsCollectionInterface(INamedTypeSymbol type)
    {
        if (!type.IsGenericType)
            return false;

        var original = type.OriginalDefinition;
        return original.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T ||
               original.SpecialType == SpecialType.System_Collections_Generic_ICollection_T ||
               original.SpecialType == SpecialType.System_Collections_Generic_IList_T ||
               (original.Name == "List" && original.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic") ||
               (original.Name == "IReadOnlyList" && original.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic") ||
               (original.Name == "IReadOnlyCollection" && original.ContainingNamespace?.ToDisplayString() == "System.Collections.Generic");
    }

    /// <summary>
    ///     Returns true for scalar-like types that should never be treated as navigation properties
    ///     (string, decimal, DateTime, Guid, Uri, etc.). Collection types are NOT well-known scalars.
    /// </summary>
    private static bool IsWellKnownType(INamedTypeSymbol type)
    {
        // Roslyn special types: string, int, bool, decimal, etc.
        if (type.SpecialType != SpecialType.None)
            return true;

        var ns = type.ContainingNamespace?.ToDisplayString();
        if (ns is null)
            return false;

        // System.Collections.* are collections, not scalars
        if (ns.StartsWith("System.Collections", StringComparison.Ordinal))
            return false;

        return ns.StartsWith("System", StringComparison.Ordinal);
    }

    private static HashSet<string> GetExcludedInterfaceMembers(INamedTypeSymbol entityType)
    {
        var excluded = new HashSet<string>(StringComparer.Ordinal);

        foreach (var iface in entityType.AllInterfaces)
        {
            var ifaceName = iface.ToDisplayString();
            if (!ExcludedInterfaces.Any(e => ifaceName.Contains(e)))
                continue;

            foreach (var member in iface.GetMembers())
            {
                if (member is IPropertySymbol prop)
                    excluded.Add(prop.Name);
            }
        }

        return excluded;
    }
}

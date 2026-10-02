using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Property collection: entity properties, navigations, defaults, FK detection.
/// </summary>
internal static partial class EntityTransform
{
    // logicKeys is the whole set, not the first. The schema snapshot builds the unique index from
    // IsLogicKey, so marking only the first produced an index over one column while the EF
    // configuration produced one over all of them — and on RoomType that snapshot claimed one room
    // type per property. Caught by diffing the snapshot, not by any test.
    private static ImmutableArray<PropertyMetadataModel> CollectProperties(
        INamedTypeSymbol typeSymbol,
        EquatableArray<LogicKeyPart> logicKeys,
        bool isAuditable,
        bool isSoftDelete,
        bool isConcurrencyAware,
        out ImmutableArray<string> ignoredPropertyNames,
        out ImmutableArray<PrimitiveCollectionMetadataModel> primitiveCollections)
    {
        var builder = ImmutableArray.CreateBuilder<PropertyMetadataModel>();
        var ignoredBuilder = ImmutableArray.CreateBuilder<string>();
        var primitiveCollectionsBuilder = ImmutableArray.CreateBuilder<PrimitiveCollectionMetadataModel>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            // Skip static and indexer properties
            if (prop.IsStatic || prop.IsIndexer)
                continue;

            // Skip backing fields
            if (prop.Name.StartsWith("<", StringComparison.Ordinal))
                continue;

            // Collections of primitives (List<string>, string[], IEnumerable<int>, …) persist as EF Core
            // primitive collections (JSON column), NOT as navigations. Record them separately so the
            // EntityConfiguration template can emit builder.Property(...). AccessScopes ([HasAccessScopes]) is
            // generated separately and excluded here to avoid a duplicate mapping.
            // NB: do NOT `continue` — a primitive collection is also a settable scalar-like property and must
            // flow through to the normal PropertyMetadataModel below so it gets a Set{X} setter, change
            // tracking, and mutation ApplyToEntity mapping. We only keep it out of the navigation check.
            var isPrimitiveCollection = IsPrimitiveCollectionProperty(prop) && prop.Name != "AccessScopes";
            if (isPrimitiveCollection)
            {
                primitiveCollectionsBuilder.Add(new PrimitiveCollectionMetadataModel
                {
                    Name = prop.Name,
                    IsNullable = prop.NullableAnnotation == NullableAnnotation.Annotated
                });
            }

            // Skip navigation properties (collections or reference types with FK) — but a primitive
            // collection is NOT a navigation.
            if (!isPrimitiveCollection && IsNavigationProperty(prop))
            {
                ignoredBuilder.Add(prop.Name);
                continue;
            }

            // Skip read-only properties (expression-bodied or getter-only).
            // For PE metadata symbols (referenced assemblies), SetMethod is null for
            // properties with non-public setters (private/internal) because the setter
            // is not accessible from outside the assembly. We include these properties
            // so that type conversions (e.g., LocalizedString), constraints, and
            // other EF Core configuration can be applied at host level.
            var setMethod = prop.SetMethod;
            var hasSetter = setMethod is not null;
            if (!hasSetter)
            {
                var isFromMetadata = !typeSymbol.Locations.Any(l => l.IsInSource);
                if (!isFromMetadata)
                    continue;

                // For PE metadata symbols: SetMethod is null for BOTH getter-only AND
                // private-setter properties. Convention: entity 'Id' is always expression-bodied.
                if (prop.Name == "Id")
                    continue;

                // Distinguish auto-properties from expression-bodied properties
                var getter = prop.GetMethod;
                var isAutoProperty = getter is not null &&
                    getter.GetAttributes().Any(a =>
                        a.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.CompilerGeneratedAttribute");
                if (!isAutoProperty)
                    continue;
            }

            var hasPrivateSetter = setMethod is null ||
                                    setMethod.DeclaredAccessibility != Accessibility.Public;

            var isNullable = prop.NullableAnnotation == NullableAnnotation.Annotated ||
                             prop.Type is { IsValueType: true, OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };

            var isLogicKeyProp = logicKeys.AsImmutableArray().Any(k => k.Name == prop.Name);

            // FK properties (e.g., InvoiceId with matching Invoice navigation) are excluded
            // from required-for-create so that EF Core can resolve them via navigation fixup
            var isForeignKey = prop.Name.EndsWith("Id", StringComparison.Ordinal)
                               && prop.Name.Length > 2
                               && prop.Name != "PersistenceId"
                               && typeSymbol.GetMembers().Any(m =>
                                   m is IPropertySymbol nav
                                   && nav.Name == prop.Name.Substring(0, prop.Name.Length - 2)
                                   && !nav.Type.IsValueType);

            var isRequiredForCreate = !isNullable &&
                                      !HasDefaultValue(prop) &&
                                      !isForeignKey &&
                                      prop.Name != "PersistenceId" &&
                                      prop.Name != "Id" &&
                                      !(isAuditable && AuditProps.Contains(prop.Name)) &&
                                      !(isSoftDelete && SoftDeleteProps.Contains(prop.Name)) &&
                                      !(isConcurrencyAware && ConcurrencyProps.Contains(prop.Name));

            var defaultValueExpr = GetDefaultValueExpression(prop);
            var computedDefaultFqn = GetComputedDefaultGeneratorFqn(prop);
            var hasDefault = HasDefaultValue(prop) || defaultValueExpr is not null || computedDefaultFqn is not null;

            if (hasDefault && !HasDefaultValue(prop))
                isRequiredForCreate = false;

            var renamedFrom = GetRenamedFrom(prop);

            builder.Add(new PropertyMetadataModel
            {
                Name = prop.Name,
                TypeName = prop.Type.ToDisplayString(),
                HasPrivateSetter = hasPrivateSetter,
                IsNullable = isNullable,
                IsEnum = prop.Type.TypeKind == TypeKind.Enum ||
                         (prop.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
                          prop.Type is INamedTypeSymbol namedNullable && namedNullable.TypeArguments.Length == 1 && namedNullable.TypeArguments[0].TypeKind == TypeKind.Enum),
                IsLogicKey = isLogicKeyProp,
                HasDefaultValue = hasDefault,
                IsNavigation = false,
                IsRequiredForCreate = isRequiredForCreate,
                IsRequiredMember = prop.IsRequired,
                DefaultValueExpression = defaultValueExpr,
                ComputedDefaultGeneratorFqn = computedDefaultFqn,
                RenamedFrom = renamedFrom,
                WireName = Core.WireNameReader.Read(prop),
                ValueObjectColumns = ComputeValueObjectColumns(prop)
            });
        }

        ignoredPropertyNames = ignoredBuilder.ToImmutable();
        primitiveCollections = primitiveCollectionsBuilder.ToImmutable();
        return builder.ToImmutable();
    }

    /// <summary>
    ///     Determines whether a property is a collection of primitive/scalar values
    ///     (<c>List&lt;string&gt;</c>, <c>string[]</c>, <c>IEnumerable&lt;int&gt;</c>, …).
    ///     EF Core 8+ persists these as a primitive collection (JSON column), so they must NOT be
    ///     treated as navigations. Collections of entity classes remain navigations.
    /// </summary>
    /// <remarks>
    ///     Internal rather than private because <c>LoadingProfileTransform</c> asks the same question:
    ///     what on this entity is a navigation EF Core can <c>Include</c>. A cruder copy of its own —
    ///     every generic collection and every non-string reference type — would turn a JSON primitive
    ///     collection and a value object into include paths, and the generated <c>ApplyIncludes()</c>
    ///     would throw before it read a row. One rule, two readers.
    /// </remarks>
    internal static bool IsPrimitiveCollectionProperty(IPropertySymbol prop)
    {
        var type = prop.Type;

        ITypeSymbol? elementType = type switch
        {
            // T[] (string[], int[], …) — but byte[] is a scalar (binary blob), not a collection.
            IArrayTypeSymbol array when array.ElementType.SpecialType != SpecialType.System_Byte
                => array.ElementType,

            // List<T>, IEnumerable<T>, ICollection<T>, IReadOnlyList<T>, … from System.Collections.Generic
            INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } named
                when named.OriginalDefinition.ToDisplayString()
                    .StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                => named.TypeArguments[0],

            _ => null
        };

        return elementType is not null && IsPrimitiveElementType(elementType);
    }

    /// <summary>
    ///     Whether a collection element type is a primitive/scalar EF Core can store in a JSON primitive
    ///     collection (string, numeric, bool, enum, Guid, date/time types).
    /// </summary>
    private static bool IsPrimitiveElementType(ITypeSymbol type)
    {
        // Unwrap Nullable<T> (e.g. List<int?>)
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            type = nullable.TypeArguments[0];

        if (type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_String)
            return true;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        var name = type.ToDisplayString();
        return name is "System.Guid" or "System.DateTime" or "System.DateTimeOffset"
            or "System.TimeSpan" or "System.DateOnly" or "System.TimeOnly" or "System.Decimal";
    }

    /// <summary>
    ///     Collects ALL property names from the type symbol (including navigations and filtered properties).
    ///     Used for deduplication in RelationGraphBuilder.
    /// </summary>
    private static ImmutableArray<string> CollectAllPropertyNames(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: false, IsIndexer: false } prop &&
                !prop.Name.StartsWith("<", StringComparison.Ordinal))
            {
                builder.Add(prop.Name);
            }
        }

        return builder.ToImmutable();
    }

    private static bool HasDefaultValue(IPropertySymbol prop)
    {
        foreach (var syntaxRef in prop.DeclaringSyntaxReferences)
        {
            if (syntaxRef.GetSyntax() is PropertyDeclarationSyntax { Initializer: not null }) return true;
        }

        return false;
    }

    /// <summary>
    ///     Gets the static default value expression from [DefaultValue(...)], if present.
    /// </summary>
    private static string? GetDefaultValueExpression(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var fqn = attrClass.ToDisplayString();
            if (fqn != DefaultValueAttributeName)
                continue;

            if (attr.ConstructorArguments.Length > 0)
                return ToLiteral(attr.ConstructorArguments[0]);
        }

        return null;
    }

    /// <summary>
    ///     Renders an attribute argument as a C# literal that compiles.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>value.ToString()</c> is not that literal, and the difference is not cosmetic: a
    ///         <c>bool</c> comes out as <c>True</c>, which does not compile; a <c>decimal</c> comes out
    ///         in the current culture, so the same source generates <c>1,5</c> on an Italian machine and
    ///         <c>1.5</c> on an English one; a <c>string</c> comes out unquoted and unescaped.
    ///     </para>
    ///     <para>
    ///         With <c>ToString()</c>, a plain <c>[DefaultValue(true)]</c> would produce
    ///         <c>IsActive = True,</c> inside the generated <c>Create</c> factory.
    ///     </para>
    /// </remarks>
    private static string? ToLiteral(TypedConstant arg)
    {
        if (arg.IsNull || arg.Value is null)
            return null;

        // An enum argument arrives as its underlying number: cast it back, or the literal is an int
        // where the property is an enum.
        if (arg.Type is { TypeKind: TypeKind.Enum } enumType)
        {
            var qualified = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"({qualified}){SymbolDisplay.FormatPrimitive(arg.Value, quoteStrings: false, useHexadecimalNumbers: false)}";
        }

        return SymbolDisplay.FormatPrimitive(arg.Value, quoteStrings: true, useHexadecimalNumbers: false);
    }

    /// <summary>
    ///     Gets the FQN of the computed default generator from [ComputedDefault&lt;TEntity, TValue, TGenerator&gt;].
    /// </summary>
    private static string? GetComputedDefaultGeneratorFqn(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null)
                continue;

            var originalDef = attrClass.OriginalDefinition.ToDisplayString();
            if (originalDef != ComputedDefaultAttributePrefix)
                continue;

            if (attrClass is INamedTypeSymbol { TypeArguments.Length: 3 } namedType)
            {
                return namedType.TypeArguments[2].ToDisplayString();
            }
        }

        return null;
    }

    /// <summary>
    ///     Whether the property is a navigation EF Core can <c>Include</c>.
    /// </summary>
    /// <remarks>
    ///     Asked together with <see cref="IsPrimitiveCollectionProperty" />, never alone: a
    ///     <c>List&lt;string&gt;</c> satisfies the collection branch below and is a JSON column, not a
    ///     navigation. The pairing is the rule — see the caller in <c>CollectProperties</c>, and the
    ///     second reader in <c>LoadingProfileTransform</c>.
    /// </remarks>
    internal static bool IsNavigationProperty(IPropertySymbol prop)
    {
        var type = prop.Type;

        // Collection types (all generic collections that EF Core interprets as navigations)
        if (type is INamedTypeSymbol { IsGenericType: true } namedType)
        {
            var genericDef = namedType.OriginalDefinition.ToDisplayString();
            if (genericDef.StartsWith("System.Collections.Generic.", StringComparison.Ordinal))
                return true;
        }

        // Interface types are never database-mappable scalar properties
        if (type.TypeKind == TypeKind.Interface)
            return true;

        // Reference types with corresponding FK property
        if (type is { IsReferenceType: true, TypeKind: TypeKind.Class })
        {
            var fkName = prop.Name + "Id";
            if (prop.ContainingType.GetMembers(fkName).Any())
                return true;

            // Owned entity detection
            if (type is INamedTypeSymbol classType && InheritsFrom(classType, "IdentityRecord", "Pragmatic.Identity"))
                return true;
        }

        return false;
    }

    private static ImmutableArray<NavigationMetadataModel> CollectNavigations(INamedTypeSymbol typeSymbol)
    {
        var builder = ImmutableArray.CreateBuilder<NavigationMetadataModel>();

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            if (prop.IsStatic || prop.IsIndexer)
                continue;

            if (!IsNavigationProperty(prop))
                continue;

            var type = prop.Type;
            var isCollection = false;
            string targetTypeName;
            string navigationType;

            if (type is INamedTypeSymbol { IsGenericType: true } namedType)
            {
                var targetType = namedType.TypeArguments[0];

                // Skip collections whose element type is not a class
                if (targetType.TypeKind != TypeKind.Class || targetType.SpecialType == SpecialType.System_String)
                    continue;

                isCollection = true;
                targetTypeName = targetType.ToDisplayString();
                navigationType = "OneToMany";
            }
            else
            {
                targetTypeName = type.ToDisplayString().TrimEnd('?');
                navigationType = "ManyToOne";
            }

            var targetSymbol = isCollection && type is INamedTypeSymbol collectionType
                ? (collectionType.TypeArguments[0] as INamedTypeSymbol)
                : (type as INamedTypeSymbol);
            var targetBoundary = targetSymbol is not null
                ? FindBelongsToAttribute(targetSymbol)
                : null;

            var isOwned = !isCollection && targetSymbol is not null && InheritsFrom(targetSymbol, "IdentityRecord", "Pragmatic.Identity");

            // Only owned types are read from the shape of a property. Inferring a hand-written
            // navigation or foreign key here — name and type, every option at its default — would make
            // a second, half-working way to declare a relationship: relationships are [Relation.*], and
            // a hand-written one is reported instead.
            if (!isOwned)
                continue;

            builder.Add(new NavigationMetadataModel
            {
                Name = prop.Name,
                TargetTypeName = targetTypeName,
                TargetFullTypeName = targetTypeName,
                TargetBoundaryTypeFullName = targetBoundary,
                NavigationType = isOwned ? "Owned" : navigationType,
                ForeignKeyProperty = isCollection || isOwned ? null : prop.Name + "Id",
                IsOwned = isOwned,
                // EF maps an owned type behind a nullable navigation as an optional dependent: NULL in
                // every column when the navigation is null. The schema reads this to agree.
                // Unannotated (nullable context off) is optional too — nothing says it is required.
                IsRequired = prop.NullableAnnotation == NullableAnnotation.NotAnnotated
            });
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Checks if a type inherits from a specific base class.
    /// </summary>
    private static bool InheritsFrom(INamedTypeSymbol symbol, string baseName, string baseNamespace)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (current.Name == baseName &&
                current.ContainingNamespace?.ToDisplayString() == baseNamespace)
                return true;
            current = current.BaseType;
        }
        return false;
    }

    /// <summary>
    ///     Finds [BelongsTo&lt;T&gt;] on the entity type to determine its boundary.
    /// </summary>
    private static string? FindBelongsToAttribute(INamedTypeSymbol symbol)
        => BoundaryOwnershipReader.BoundaryOf(symbol).FullTypeName;
}

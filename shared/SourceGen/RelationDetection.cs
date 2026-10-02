// ReSharper disable once CheckNamespace

using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGen;

/// <summary>
///     Shared utility for detecting and classifying [Relation.*] attributes on entity types.
///     Used by both Persistence.EFCore.SG and Endpoints.SG (AutoCrud).
/// </summary>
internal static class RelationDetection
{
    private const string RelationContainerName = "Relation";
    private const string RelationNamespace = "Pragmatic.Persistence.Entity";
    private const string WithNavigationName = "WithNavigation";

    /// <summary>
    ///     Checks if the given entity type has any [Relation.*] attributes.
    /// </summary>
    public static bool HasRelationAttributes(INamedTypeSymbol typeSymbol)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            if (IsRelationAttribute(attr))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Checks if the given attribute is a Relation attribute (any variant).
    /// </summary>
    public static bool IsRelationAttribute(AttributeData attr)
    {
        var cls = attr.AttributeClass;
        if (cls is null)
            return false;

        return ClassifyRelationType(cls) is not null;
    }

    /// <summary>
    ///     Enumerates every [Relation.*] attribute on the type together with its parsed info.
    ///     The <see cref="AttributeData" /> is handed back so callers can report a diagnostic on the
    ///     attribute itself rather than on the whole type.
    /// </summary>
    public static IEnumerable<(AttributeData Attribute, RelationInfo Info)> GetRelations(INamedTypeSymbol typeSymbol)
    {
        foreach (var attr in typeSymbol.GetAttributes())
        {
            var info = ClassifyRelationAttribute(attr);
            if (info is not null)
                yield return (attr, info);
        }
    }

    /// <summary>
    ///     The navigation property name a [Relation.*] actually produces: the explicit
    ///     <c>WithNavigation("...")</c> name, or the convention-derived one.
    /// </summary>
    /// <remarks>
    ///     Single source of truth for the convention. Validation compares names the generator will
    ///     emit, so deriving them a second time elsewhere would let a diagnostic judge a name that
    ///     is never generated.
    /// </remarks>
    public static string GetNavigationName(RelationInfo info)
        => info.NavigationName ?? info.RelationType switch
        {
            // Collections get pluralized names; reference navs use the type name directly.
            "OneToMany" or "ManyToMany" => StringHelper.Pluralize(info.TargetType.Name),
            _ => info.TargetType.Name
        };

    /// <summary>
    ///     Classifies a Relation attribute, extracting relation type, target type, and configuration.
    ///     Returns null if the attribute is not a Relation attribute.
    /// </summary>
    public static RelationInfo? ClassifyRelationAttribute(AttributeData attr)
    {
        var cls = attr.AttributeClass;
        if (cls is null)
            return null;

        var isWithNavigation = cls.Name == WithNavigationName;

        // Get the relation type class (OneToMany<T>, ManyToOne<T>, etc.)
        INamedTypeSymbol? relationTypeClass;
        if (isWithNavigation)
        {
            // WithNavigation is nested inside the relation type class
            relationTypeClass = cls.ContainingType;
        }
        else
        {
            relationTypeClass = cls;
        }

        if (relationTypeClass is null)
            return null;

        // Verify this is inside the Relation container
        var relationType = ClassifyRelationType(relationTypeClass);
        if (relationType is null)
            return null;

        // Extract target type from type arguments
        if (relationTypeClass.TypeArguments.Length == 0)
            return null;
        var targetType = relationTypeClass.TypeArguments[0] as INamedTypeSymbol;
        if (targetType is null)
            return null;

        // For ManyToMany<T, TJoinEntity>, extract join entity
        INamedTypeSymbol? joinEntityType = null;
        if (relationTypeClass.TypeArguments.Length > 1)
        {
            joinEntityType = relationTypeClass.TypeArguments[1] as INamedTypeSymbol;
        }

        // Extract WithNavigation properties (constructor args + named args)
        string? navName = null;
        string? inverse = null;
        string? foreignKey = null;
        string onDelete = GetDefaultOnDelete(relationType);
        var isRequired = true;
        var isPrincipal = false;
        string? joinTable = null;
        string? leftKey = null;
        string? rightKey = null;

        if (isWithNavigation)
        {
            // Constructor argument: name
            if (attr.ConstructorArguments.Length > 0)
                navName = attr.ConstructorArguments[0].Value as string;

            foreach (var kvp in attr.NamedArguments)
            {
                switch (kvp.Key)
                {
                    case "Inverse":
                        inverse = kvp.Value.Value as string;
                        break;
                    case "ForeignKey":
                        foreignKey = kvp.Value.Value as string;
                        break;
                    case "OnDelete":
                        onDelete = ResolveDeleteBehavior(kvp.Value);
                        break;
                    case "Required":
                        if (kvp.Value.Value is bool reqVal)
                            isRequired = reqVal;
                        break;
                    case "IsPrincipal":
                        if (kvp.Value.Value is bool prinVal)
                            isPrincipal = prinVal;
                        break;
                    case "JoinTable":
                        joinTable = kvp.Value.Value as string;
                        break;
                    case "LeftKey":
                        leftKey = kvp.Value.Value as string;
                        break;
                    case "RightKey":
                        rightKey = kvp.Value.Value as string;
                        break;
                }
            }
        }

        return new RelationInfo(
            RelationType: relationType,
            TargetType: targetType,
            JoinEntityType: joinEntityType,
            IsWithNavigation: isWithNavigation,
            NavigationName: navName,
            InverseProperty: inverse,
            ForeignKeyProperty: foreignKey,
            OnDelete: onDelete,
            IsRequired: isRequired,
            IsPrincipal: isPrincipal,
            JoinTable: joinTable,
            LeftKey: leftKey,
            RightKey: rightKey);
    }

    /// <summary>
    ///     Determines the relation type from a type symbol that is a direct child of Relation.
    /// </summary>
    private static string? ClassifyRelationType(INamedTypeSymbol symbol)
    {
        // For WithNavigation: check the containing type chain
        if (symbol.Name == WithNavigationName)
        {
            var parent = symbol.ContainingType;
            if (parent is null)
                return null;
            return ClassifyRelationType(parent);
        }

        // Check if this type is directly inside the Relation container
        var container = symbol.ContainingType;
        if (container?.Name != RelationContainerName)
            return null;

        // Verify namespace
        var ns = container.ContainingNamespace?.ToDisplayString();
        if (ns != RelationNamespace)
            return null;

        return symbol.OriginalDefinition.Name switch
        {
            "OneToOne" => "OneToOne",
            "OneToMany" => "OneToMany",
            "ManyToOne" => "ManyToOne",
            "ManyToMany" => "ManyToMany",
            _ => null
        };
    }

    private static string GetDefaultOnDelete(string relationType)
    {
        return relationType switch
        {
            "OneToMany" => "Cascade",
            "ManyToOne" => "Restrict",
            "OneToOne" => "Restrict",
            "ManyToMany" => "Cascade",
            _ => "NoAction"
        };
    }

    /// <summary>
    ///     Resolves a DeleteBehavior enum value from a TypedConstant.
    /// </summary>
    private static string ResolveDeleteBehavior(TypedConstant constant)
    {
        if (constant.Kind != TypedConstantKind.Enum || constant.Value is null)
            return "NoAction";

        // Map the enum int value to its name
        var enumType = constant.Type as INamedTypeSymbol;
        if (enumType is null)
            return "NoAction";

        var intVal = (int)constant.Value;
        foreach (var member in enumType.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true, ConstantValue: int fieldVal } field &&
                fieldVal == intVal)
                return field.Name;
        }

        return "NoAction";
    }
}

/// <summary>
///     Parsed information from a [Relation.*] attribute.
/// </summary>
internal sealed record RelationInfo(
    string RelationType,
    INamedTypeSymbol TargetType,
    INamedTypeSymbol? JoinEntityType,
    bool IsWithNavigation,
    string? NavigationName,
    string? InverseProperty,
    string? ForeignKeyProperty,
    string OnDelete,
    bool IsRequired,
    bool IsPrincipal,
    string? JoinTable,
    string? LeftKey = null,
    string? RightKey = null);

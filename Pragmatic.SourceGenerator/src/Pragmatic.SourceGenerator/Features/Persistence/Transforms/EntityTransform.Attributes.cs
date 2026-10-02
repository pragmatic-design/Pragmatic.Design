using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Attribute parsing: trait detection, soft delete, inheritance, temporal, boundary, table mapping.
/// </summary>
internal static partial class EntityTransform
{
    /// <summary>
    ///     Detects which trait properties (PersistenceId, IAuditable, ISoftDelete, IOwnedEntity) are manually declared
    ///     and which interfaces are explicitly implemented on the entity type.
    /// </summary>
    private static ManualTraitInfo DetectManualTraits(INamedTypeSymbol typeSymbol)
    {
        var declared = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        var hasPersistenceId = false;
        var hasCreatedAt = false;
        var hasCreatedBy = false;
        var hasUpdatedAt = false;
        var hasUpdatedBy = false;
        var hasIsDeleted = false;
        var hasDeletedAt = false;
        var hasDeletedBy = false;
        var hasOwnerId = false;
        var hasAccessScopes = false;

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop || prop.IsStatic || prop.IsIndexer)
                continue;

            declared.Add(prop.Name);

            switch (prop.Name)
            {
                case "PersistenceId":
                    hasPersistenceId = true;
                    break;
                case "CreatedAt":
                    hasCreatedAt = true;
                    break;
                case "CreatedBy":
                    hasCreatedBy = true;
                    break;
                case "UpdatedAt":
                    hasUpdatedAt = true;
                    break;
                case "UpdatedBy":
                    hasUpdatedBy = true;
                    break;
                case "IsDeleted":
                    hasIsDeleted = true;
                    break;
                case "DeletedAt":
                    hasDeletedAt = true;
                    break;
                case "DeletedBy":
                    hasDeletedBy = true;
                    break;
                case "OwnerId":
                    hasOwnerId = true;
                    break;
                case "AccessScopes":
                    hasAccessScopes = true;
                    break;
            }
        }

        // Detect explicitly declared interfaces on the type declaration
        var hasEntityInterface = false;
        var hasAuditableInterface = false;
        var hasSoftDeleteInterface = false;
        var hasOwnedEntityInterface = false;
        var hasScopedEntityInterface = false;

        foreach (var iface in typeSymbol.Interfaces)
        {
            var fqn = iface.OriginalDefinition.ToDisplayString();
            if (iface.Name == "IEntity")
                hasEntityInterface = true;
            else if (fqn == "Pragmatic.Persistence.Entity.IAuditable")
                hasAuditableInterface = true;
            else if (fqn == "Pragmatic.Persistence.Entity.ISoftDelete")
                hasSoftDeleteInterface = true;
            else if (fqn == "Pragmatic.Persistence.Entity.IOwnedEntity")
                hasOwnedEntityInterface = true;
            else if (fqn == "Pragmatic.Persistence.Entity.IScopedEntity")
                hasScopedEntityInterface = true;
        }

        // Also check base type interfaces (e.g., DomainEventSource implements IEntity)
        if (!hasEntityInterface)
        {
            hasEntityInterface = typeSymbol.AllInterfaces.Any(i =>
                i.ToDisplayString() == "Pragmatic.Persistence.Entity.IEntity");
        }

        return new ManualTraitInfo
        {
            HasManualPersistenceId = hasPersistenceId,
            HasManualAuditableProps = hasCreatedAt && hasCreatedBy && hasUpdatedAt && hasUpdatedBy,
            HasManualSoftDeleteProps = hasIsDeleted && hasDeletedAt && hasDeletedBy,
            HasManualOwnedEntityProps = hasOwnerId,
            HasManualScopedEntityProps = hasAccessScopes,
            HasManualEntityInterface = hasEntityInterface,
            HasManualAuditableInterface = hasAuditableInterface,
            HasManualSoftDeleteInterface = hasSoftDeleteInterface,
            HasManualOwnedEntityInterface = hasOwnedEntityInterface,
            HasManualScopedEntityInterface = hasScopedEntityInterface,
            DeclaredTraitProperties = declared.ToImmutable()
        };
    }

    /// <summary>
    ///     The trait property groups the entity declares only part of.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The generator stands down on a group when the entity declares all of it — that is how an
    ///         application owns the shape of its own audit or soft-delete columns. Declaring some of a
    ///         group is neither: the group flag stays false, all of it is emitted, and each property the
    ///         author wrote collides with a generated twin.
    ///     </para>
    ///     <para>
    ///         On its own that is a <c>CS0102</c> per property, blaming a duplicate in a file they never
    ///         opened, with nothing to say that writing the <i>missing</i> one is the fix. PRAG0624 says
    ///         it. Only groups the entity actually has are considered: a <c>DeletedAt</c> on an entity
    ///         with no <c>[SoftDelete]</c> is just a property.
    ///     </para>
    /// </remarks>
    internal static ImmutableArray<PartialTraitModel> DetectPartialTraits(
        IReadOnlyCollection<string> declaredProperties, bool isAuditable, bool isSoftDelete)
    {
        var builder = ImmutableArray.CreateBuilder<PartialTraitModel>();

        if (isAuditable)
            AddIfPartial(builder, declaredProperties, "Auditable", AuditableTraitProperties);

        if (isSoftDelete)
            AddIfPartial(builder, declaredProperties, "SoftDelete", SoftDeleteTraitProperties);

        return builder.ToImmutable();
    }

    private static readonly string[] AuditableTraitProperties =
        ["CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy"];

    private static readonly string[] SoftDeleteTraitProperties = ["IsDeleted", "DeletedAt", "DeletedBy"];

    private static void AddIfPartial(
        ImmutableArray<PartialTraitModel>.Builder builder,
        IReadOnlyCollection<string> declaredProperties,
        string traitName,
        string[] group)
    {
        var missing = group.Where(p => !declaredProperties.Contains(p)).ToImmutableArray();

        // All present: the entity owns the trait. None present: the generator owns it. Both are fine.
        if (missing.Length == 0 || missing.Length == group.Length)
            return;

        builder.Add(new PartialTraitModel { TraitName = traitName, MissingProperties = missing });
    }

    /// <summary>
    ///     Result of detecting which trait members are manually declared by the developer.
    /// </summary>
    private sealed class ManualTraitInfo
    {
        public bool HasManualPersistenceId { get; init; }
        public bool HasManualAuditableProps { get; init; }
        public bool HasManualSoftDeleteProps { get; init; }
        public bool HasManualOwnedEntityProps { get; init; }
        public bool HasManualScopedEntityProps { get; init; }
        public bool HasManualEntityInterface { get; init; }
        public bool HasManualAuditableInterface { get; init; }
        public bool HasManualSoftDeleteInterface { get; init; }
        public bool HasManualOwnedEntityInterface { get; init; }
        public bool HasManualScopedEntityInterface { get; init; }

        /// <summary>Every instance property the entity declares itself, for the partial-trait check.</summary>
        public required ImmutableHashSet<string> DeclaredTraitProperties { get; init; }
    }

    private static bool HasAttribute(ISymbol symbol, string attributeName)
    {
        return symbol.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == attributeName);
    }

    /// <summary>
    ///     The rule types named by <c>[VisibleWhen&lt;TRule&gt;]</c> on the entity, fully qualified.
    /// </summary>
    /// <remarks>
    ///     Matched on the attribute's own name and arity rather than on
    ///     <c>ToDisplayString()</c>: a generic attribute renders with its argument in it, so comparing
    ///     the whole string would never match — the mistake that left the Mutation branch of
    ///     <c>[ExposeEndpoint]</c> unreachable.
    /// </remarks>
    private static ImmutableArray<string> GetVisibilityRules(INamedTypeSymbol typeSymbol)
    {
        var rules = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass is not { Name: "VisibleWhenAttribute", TypeArguments.Length: 1 })
                continue;

            if (attributeClass.ContainingNamespace?.ToDisplayString()
                != "Pragmatic.Persistence.Entity")
                continue;

            if (attributeClass.TypeArguments[0] is INamedTypeSymbol rule)
                rules.Add(rule.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        return rules.ToImmutable();
    }

    private static bool GetSoftDeleteCascade(INamedTypeSymbol typeSymbol)
    {
        var attr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == SoftDeleteAttributeName);
        if (attr is null)
            return false;

        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "Cascade", Value.Value: bool cascade })
                return cascade;
        }
        return false;
    }

    /// <summary>
    ///     Reads the [Inheritance] attribute and returns the strategy string (TPH/TPT/TPC) and
    ///     the custom discriminator column name, or (null, null) if absent.
    /// </summary>
    private static (string? Strategy, string? DiscriminatorColumn) GetInheritanceInfo(INamedTypeSymbol typeSymbol)
    {
        var attr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == InheritanceAttributeName);
        if (attr is null)
            return (null, null);

        if (attr.ConstructorArguments.Length == 0 || attr.ConstructorArguments[0].Value is not int strategyValue)
            return (null, null);

        var strategy = strategyValue switch
        {
            0 => "TPH",
            1 => "TPT",
            2 => "TPC",
            _ => "TPH"
        };

        string? discriminatorColumn = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "DiscriminatorColumn" && named.Value.Value is string col)
            {
                discriminatorColumn = col;
                break;
            }
        }

        return (strategy, discriminatorColumn);
    }

    /// <summary>
    ///     Reads <c>[StateMachine&lt;TEnum&gt;]</c> and resolves the enum value marked
    ///     <c>[InitialState]</c>, as the property to assign and the expression to assign to it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read here rather than taken from <c>StateMachineTransform</c> because the consumer is the
    ///         factory this pipeline generates, and a transform cannot ask another for its model.
    ///         <c>StateMachineTransform</c> keeps computing the same value for its own diagnostics; the
    ///         two readings agree by construction, both being the enum member carrying the attribute.
    ///     </para>
    ///     <para>
    ///         More than one such member is <c>PRAG0638</c>, reported by the validator on the other
    ///         pipeline. Here the first is taken, which is what the factory would have done anyway.
    ///     </para>
    /// </remarks>
    private static (string? PropertyName, string? InitialStateExpression) GetStateMachineInitialState(
        INamedTypeSymbol typeSymbol)
    {
        var attr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { IsGenericType: true } generic &&
            generic.OriginalDefinition.Name == "StateMachineAttribute" &&
            generic.OriginalDefinition.ContainingNamespace?.ToDisplayString()
                == "Pragmatic.Persistence.StateMachine");

        if (attr?.AttributeClass?.TypeArguments.FirstOrDefault() is not INamedTypeSymbol
            { TypeKind: TypeKind.Enum } enumType)
            return (null, null);

        var propertyName = "Status";
        foreach (var named in attr.NamedArguments)
        {
            if (named is { Key: "Property", Value.Value: string declared })
                propertyName = declared;
        }

        var initial = enumType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.IsConst && f.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString()
                    == "Pragmatic.Persistence.StateMachine.InitialStateAttribute"));

        if (initial is null)
            return (propertyName, null);

        return (propertyName, $"global::{enumType.ToDisplayString()}.{initial.Name}");
    }

    /// <summary>
    ///     Walks the base type chain to find the nearest base that is also an entity
    ///     (has [Entity] attribute). Returns the FQN if found, null otherwise.
    /// </summary>
    private static string? GetBaseEntityFullTypeName(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current is not null && current.SpecialType != SpecialType.System_Object)
        {
            if (HasEntityAttribute(current))
                return current.ToDisplayString();
            current = current.BaseType;
        }

        return null;
    }

    private static string? GetRenamedFrom(IPropertySymbol prop)
    {
        var attr = prop.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.Name == "RenamedFromAttribute" &&
            a.AttributeClass.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity");
        return attr?.ConstructorArguments.Length > 0 ? attr.ConstructorArguments[0].Value as string : null;
    }

    /// <summary>
    ///     Reads [Table("name", Schema = "schema")] from System.ComponentModel.DataAnnotations.Schema.
    /// </summary>
    private static (string? TableName, string? SchemaName) GetTableAttribute(INamedTypeSymbol typeSymbol)
    {
        var attr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == "System.ComponentModel.DataAnnotations.Schema.TableAttribute");
        if (attr is null)
            return (null, null);

        var tableName = attr.ConstructorArguments.Length > 0
            ? attr.ConstructorArguments[0].Value as string
            : null;

        string? schemaName = null;
        foreach (var named in attr.NamedArguments)
        {
            if (named.Key == "Schema")
            {
                schemaName = named.Value.Value as string;
                break;
            }
        }

        return (tableName, schemaName);
    }

    /// <summary>
    ///     Whether the type carries <c>[Entity]</c>.
    /// </summary>
    /// <remarks>
    ///     Matched on the symbol's name and namespace, not on <c>ToDisplayString()</c>. The attribute
    ///     exists only in generic form, so the display string is
    ///     <c>Pragmatic.Persistence.Entity.EntityAttribute&lt;System.Guid&gt;</c> — whose last dot falls
    ///     inside the type argument. <see cref="IsEntityAttributeName"/> splits on that dot, read
    ///     <c>"Guid&gt;"</c> and rejected every entity: <c>GetBaseEntityFullTypeName</c> could never
    ///     return a value, and the two things that depend on it (the setter owner for a derived entity,
    ///     and the hierarchy-root filter in <c>BuildInheritanceConfigs</c>) behaved as if no entity ever
    ///     derived from another. That helper stays as it is — it takes syntax, where the name is written
    ///     as <c>Entity&lt;Guid&gt;</c> and the split is correct.
    /// </remarks>
    private static bool HasEntityAttribute(INamedTypeSymbol typeSymbol)
    {
        return typeSymbol.GetAttributes().Any(a =>
            a.AttributeClass is { } attributeClass &&
            attributeClass.OriginalDefinition.Name == "EntityAttribute" &&
            attributeClass.OriginalDefinition.ContainingNamespace?.ToDisplayString()
                == "Pragmatic.Persistence.Entity");
    }

    private static TemporalRelationInfo GetTemporalRelationInfo(INamedTypeSymbol typeSymbol)
    {
        // Check for generic [TemporalRelation<TParent>] or [TemporalRelation<TParent, TChild>] first
        var genericAttr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { IsGenericType: true, OriginalDefinition.Name: "TemporalRelationAttribute" } ac &&
            ac.OriginalDefinition.ContainingNamespace?.ToDisplayString() == "Pragmatic.Persistence.Entity");

        if (genericAttr is not null)
        {
            var parentType = genericAttr.AttributeClass!.TypeArguments[0];
            var childType = genericAttr.AttributeClass!.TypeArguments.Length > 1
                ? genericAttr.AttributeClass!.TypeArguments[1]
                : null;

            var maxActive = 0;
            var allowOverlap = false;
            foreach (var namedArg in genericAttr.NamedArguments)
            {
                if (namedArg is { Key: "MaxActive", Value.Value: int max })
                    maxActive = max;
                if (namedArg is { Key: "AllowOverlap", Value.Value: bool overlap })
                    allowOverlap = overlap;
            }

            // The keys come from the declared relations to the two ends, not from a member found by
            // name: the generated key lives in a file this transform cannot see, so looking for it
            // among the members found only a hand-written one — and required exactly that.
            var parentKey = parentType is INamedTypeSymbol namedParent
                ? Core.TraitPropertyResolver.DeclaredForeignKeyTo(typeSymbol, namedParent)
                : null;
            var childKey = childType is INamedTypeSymbol namedChild
                ? Core.TraitPropertyResolver.DeclaredForeignKeyTo(typeSymbol, namedChild)
                : null;
            var parentFk = parentKey?.Name;
            var childFk = childKey?.Name;
            var hasParentFk = parentKey is not null;
            var hasChildFk = childKey is not null;

            return new TemporalRelationInfo
            {
                IsTemporalRelation = true,
                MaxActive = maxActive,
                AllowOverlap = allowOverlap,
                ParentTypeName = parentType.Name,
                ParentTypeFullName = parentType.ToDisplayString().TrimEnd('?'),
                ChildTypeName = childType?.Name,
                ChildTypeFullName = childType?.ToDisplayString().TrimEnd('?'),
                ParentFkProperty = hasParentFk ? parentFk : null,
                ChildFkProperty = hasChildFk ? childFk : null
            };
        }

        // Fallback: non-generic [TemporalRelation]
        var attr = typeSymbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == TemporalRelationAttributeName);
        if (attr is null)
            return TemporalRelationInfo.None;

        var maxActive2 = 0;
        var allowOverlap2 = false;
        foreach (var namedArg in attr.NamedArguments)
        {
            if (namedArg is { Key: "MaxActive", Value.Value: int max })
                maxActive2 = max;
            if (namedArg is { Key: "AllowOverlap", Value.Value: bool overlap })
                allowOverlap2 = overlap;
        }

        return new TemporalRelationInfo
        {
            IsTemporalRelation = true,
            MaxActive = maxActive2,
            AllowOverlap = allowOverlap2
        };
    }

    /// <summary>
    ///     Result of parsing [TemporalRelation] or [TemporalRelation&lt;TParent, TChild&gt;].
    /// </summary>
    private sealed class TemporalRelationInfo
    {
        public static readonly TemporalRelationInfo None = new();
        public bool IsTemporalRelation { get; init; }
        public int MaxActive { get; init; }
        public bool AllowOverlap { get; init; }
        public string? ParentTypeName { get; init; }
        public string? ParentTypeFullName { get; init; }
        public string? ChildTypeName { get; init; }
        public string? ChildTypeFullName { get; init; }
        public string? ParentFkProperty { get; init; }
        public string? ChildFkProperty { get; init; }
    }

    private static bool ImplementsInterface(INamedTypeSymbol typeSymbol, string interfaceName)
    {
        return typeSymbol.AllInterfaces.Any(i =>
            i.ToDisplayString() == interfaceName);
    }

    /// <summary>
    ///     Which boundary owns this entity: the declared <c>[BelongsTo&lt;T&gt;]</c>, else the single
    ///     <c>[Boundary]</c> of its assembly.
    /// </summary>
    /// <remarks>
    ///     Callers delegate here rather than reading only the attribute. The entity model's
    ///     own boundary is filled again later by <c>EntityBoundaryResolver</c>, which sees the whole
    ///     compilation and can also honour <c>[Owns&lt;T&gt;]</c>; this answers for everyone who holds a
    ///     symbol and no model.
    /// </remarks>
    internal static (string? Name, string? FullTypeName) GetBoundaryInfo(INamedTypeSymbol typeSymbol)
    {
        var (fullTypeName, shortName) = Core.BoundaryOwnershipReader.BoundaryOf(typeSymbol);
        return (shortName, fullTypeName);
    }
}

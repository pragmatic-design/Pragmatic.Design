using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Validation;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transform for reading [Entity] attributes from the current compilation.
///     This supplements EntityMetadataReader which reads from referenced assemblies.
/// </summary>
internal static partial class EntityTransform
{
    public const string EntityAttributeName = "Pragmatic.Persistence.Entity.EntityAttribute`1";
    public const string EntityAttributeNonGenericName = "Pragmatic.Persistence.Entity.EntityAttribute";
    private const string LogicKeyAttributeName = "Pragmatic.Persistence.Entity.LogicKeyAttribute";
    private const string UniqueAttributeName = "Pragmatic.Persistence.Entity.UniqueAttribute";
    private const string AuditableAttributeName = "Pragmatic.Persistence.Entity.AuditableAttribute";
    private const string AuditedAttributeName = "Pragmatic.Persistence.Entity.AuditedAttribute";
    private const string DataSubjectAttributeName = "Pragmatic.Privacy.DataSubjectAttribute";
    private const string SoftDeleteAttributeName = "Pragmatic.Persistence.Entity.SoftDeleteAttribute";
    private const string ConcurrencyAwareAttributeName = "Pragmatic.Persistence.Entity.ConcurrencyAwareAttribute";
    private const string TemporalRelationAttributeName = "Pragmatic.Persistence.Entity.TemporalRelationAttribute";
    private const string TemporalRelationGeneric1AttributeName = "Pragmatic.Persistence.Entity.TemporalRelationAttribute`1";
    private const string TemporalRelationGeneric2AttributeName = "Pragmatic.Persistence.Entity.TemporalRelationAttribute`2";
    private const string HasOwnerAttributeName = "Pragmatic.Persistence.Entity.HasOwnerAttribute";
    private const string HasAccessScopesAttributeName = "Pragmatic.Persistence.Entity.HasAccessScopesAttribute";
    private const string OwnedEntityInterfaceName = "Pragmatic.Persistence.Entity.IOwnedEntity";
    private const string ScopedEntityInterfaceName = "Pragmatic.Persistence.Entity.IScopedEntity";
    private const string TenantEntityInterfaceName = "Pragmatic.MultiTenancy.ITenantEntity";

    // Static readonly to avoid per-call allocations in CollectProperties (called once per entity)
    private static readonly HashSet<string> AuditProps =
        new(StringComparer.Ordinal) { "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy" };

    private static readonly HashSet<string> SoftDeleteProps =
        new(StringComparer.Ordinal) { "IsDeleted", "DeletedAt", "DeletedBy" };

    private static readonly HashSet<string> ConcurrencyProps =
        new(StringComparer.Ordinal) { "RowVersion" };

    private static readonly HashSet<string> TemporalRelationProps =
        new(StringComparer.Ordinal) { "ValidFrom", "ValidTo" };

    private const string LookupAttributeName = "Pragmatic.Persistence.Entity.LookupAttribute";
    private const string GenerateTimelineAttributeName = "Pragmatic.Persistence.Entity.GenerateTimelineAttribute";

    private const string InheritanceAttributeName = "Pragmatic.Persistence.Entity.InheritanceAttribute";

    private const string DefaultValueAttributeName = "Pragmatic.Persistence.Entity.DefaultValueAttribute";
    private const string ComputedDefaultAttributePrefix = "Pragmatic.Persistence.Entity.ComputedDefaultAttribute`3";

    /// <summary>
    ///     Every <c>[LogicKey]</c> property, in declaration order.
    /// </summary>
    /// <remarks>
    ///     Declaration order is the contract: it is the column order of the unique index and the
    ///     parameter order of the generated lookup, so a developer reordering the properties reorders
    ///     both. Reading them in any other order would make the index they get depend on something they
    ///     cannot see.
    /// </remarks>
    /// <summary>
    ///     The parts of the logic key, in the order they belong in the index and in the generated
    ///     lookup's parameter list.
    /// </summary>
    /// <remarks>
    ///     <c>OrderBy</c> is a stable sort, so parts that leave <c>Order</c> unset keep the order they
    ///     are declared in — which is what every entity that predates the property already relies on.
    ///     Sorting unstably here would silently move the columns of existing indexes.
    /// </remarks>
    internal static EquatableArray<LogicKeyPart> CollectLogicKeys(INamedTypeSymbol typeSymbol)
    {
        // The class form names the parts; it exists for keys the class does not declare, which no
        // property-level attribute can reach. When both forms are present the class one is read and
        // PRAG0637 says the other has to go — a silent winner would be a silent index.
        var onClass = typeSymbol.GetAttributes().FirstOrDefault(
            a => a.AttributeClass?.ToDisplayString() == LogicKeyAttributeName);
        if (onClass is not null)
            return CollectClassLevelLogicKey(typeSymbol, onClass);

        var parts = new List<(int Order, LogicKeyPart Part)>();
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop)
                continue;

            var attribute = prop.GetAttributes().FirstOrDefault(
                a => a.AttributeClass?.ToDisplayString() == LogicKeyAttributeName);
            if (attribute is null)
                continue;

            var order = attribute.NamedArguments
                .FirstOrDefault(a => a.Key == "Order")
                .Value.Value as int? ?? 0;

            // UniquenessScope.Global == 1. Unset means PerTenant, which is what an unannotated key
            // needs.
            var isGlobal = attribute.NamedArguments
                .FirstOrDefault(a => a.Key == "Scope")
                .Value.Value as int? == 1;

            parts.Add((order, new LogicKeyPart
            {
                Name = prop.Name,
                TypeName = prop.Type.ToDisplayString(),
                IsGlobal = isGlobal
            }));
        }

        return parts.OrderBy(p => p.Order).Select(p => p.Part).ToImmutableArray();
    }

    /// <summary>
    ///     The parts a class-level <c>[LogicKey("A", "B")]</c> names, in the order written.
    /// </summary>
    /// <remarks>
    ///     A name resolves here against the properties the class declares and the foreign keys its own
    ///     <c>[Relation.*]</c> generate. A key the parent's relation puts on this entity is known only
    ///     to the relation graph, so its part leaves with an empty type and
    ///     <see cref="LogicKeyPartResolver" /> fills it once the graph has run; a part still empty
    ///     after that is <c>PRAG0636</c>.
    /// </remarks>
    private static EquatableArray<LogicKeyPart> CollectClassLevelLogicKey(
        INamedTypeSymbol typeSymbol,
        AttributeData attribute)
    {
        var names = attribute.ConstructorArguments.Length > 0
            ? attribute.ConstructorArguments[0].Values
                .Select(v => v.Value as string)
                .Where(v => !string.IsNullOrEmpty(v))
                .Select(v => v!)
                .ToImmutableArray()
            : ImmutableArray<string>.Empty;

        // UniquenessScope.Global == 1; one attribute, one scope for every part.
        var isGlobal = attribute.NamedArguments
            .FirstOrDefault(a => a.Key == "Scope")
            .Value.Value as int? == 1;

        var declared = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol { IsStatic: false, IsIndexer: false } prop && !declared.ContainsKey(prop.Name))
                declared.Add(prop.Name, prop.Type.ToDisplayString());
        }

        var ownKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in Core.TraitPropertyResolver.GetRelationForeignKeys(typeSymbol))
        {
            if (!ownKeys.ContainsKey(key.Name))
                ownKeys.Add(key.Name, key.TypeFullName);
        }

        var parts = ImmutableArray.CreateBuilder<LogicKeyPart>(names.Length);
        foreach (var name in names)
        {
            var typeName = declared.TryGetValue(name, out var declaredType)
                ? declaredType
                : ownKeys.TryGetValue(name, out var keyType)
                    ? keyType
                    : string.Empty;

            parts.Add(new LogicKeyPart { Name = name, TypeName = typeName, IsGlobal = isGlobal });
        }

        return parts.ToImmutable();
    }

    /// <summary>Whether the entity declares <c>[LogicKey]</c> both on the class and on a property.</summary>
    private static bool HasLogicKeyInTwoPlaces(INamedTypeSymbol typeSymbol)
    {
        var onClass = typeSymbol.GetAttributes()
            .Any(a => a.AttributeClass?.ToDisplayString() == LogicKeyAttributeName);
        if (!onClass)
            return false;

        foreach (var member in typeSymbol.GetMembers())
        {
            if (member is IPropertySymbol prop
                && prop.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == LogicKeyAttributeName))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     The unique indexes declared with <c>[Unique]</c>, in the order the attributes appear.
    /// </summary>
    /// <remarks>
    ///     Names that match no property are kept rather than dropped: the index is emitted, the
    ///     compiler rejects the generated configuration, and <c>PRAG0627</c> says which name was wrong.
    ///     Dropping them silently would leave an entity with a constraint the author declared, believes
    ///     in, and does not have.
    /// </remarks>
    private static EquatableArray<UniqueIndexModel> CollectUniqueIndexes(INamedTypeSymbol typeSymbol)
    {
        var indexes = ImmutableArray.CreateBuilder<UniqueIndexModel>();

        foreach (var attribute in typeSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != UniqueAttributeName)
                continue;

            var columns = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Values
                    .Select(v => v.Value as string)
                    .Where(v => !string.IsNullOrEmpty(v))
                    .Select(v => v!)
                    .ToImmutableArray()
                : ImmutableArray<string>.Empty;

            if (columns.Length == 0)
                continue;

            // UniquenessScope.Global == 1.
            var isGlobal = attribute.NamedArguments
                .FirstOrDefault(a => a.Key == "Scope")
                .Value.Value as int? == 1;

            indexes.Add(new UniqueIndexModel { Columns = columns, IsGlobal = isGlobal });
        }

        return indexes.ToImmutable();
    }

    /// <summary>
    ///     Checks if a syntax node is potentially an entity class (has [Entity] or [Entity] attribute).
    ///     Tight filter to avoid unnecessary semantic resolution on unrelated types.
    /// </summary>
    public static bool IsPotentialEntityClass(SyntaxNode node)
    {
        if (node is not TypeDeclarationSyntax typeDecl)
            return false;

        foreach (var attrList in typeDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (IsEntityAttributeName(name))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Matches only the [Entity] / [EntityAttribute] names, plain or qualified.
    ///     Rejects false positives like "EntityFrameworkAttribute", "MyEntityConfig", etc.
    /// </summary>
    private static bool IsEntityAttributeName(string name)
        => name is "Entity" or "EntityAttribute"
           || name.EndsWith(".Entity", StringComparison.Ordinal)
           || name.EndsWith(".EntityAttribute", StringComparison.Ordinal);

    /// <summary>
    ///     Transforms a syntax context into EntityMetadataModel if it has [Entity] attribute.
    /// </summary>
    public static EntityMetadataModel? TransformFromSyntax(
        GeneratorSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.Node is not TypeDeclarationSyntax typeDecl)
            return null;

        var semanticModel = context.SemanticModel;
        var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, ct) as INamedTypeSymbol;
        if (typeSymbol is null)
            return null;

        // Look for Entity attribute
        var entityAttr = typeSymbol.GetAttributes().FirstOrDefault(a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
                return false;

            // Check for generic Entity<T> attribute
            if (attrClass.IsGenericType)
            {
                var def = attrClass.OriginalDefinition;
                return def.ToDisplayString() == EntityAttributeName ||
                       def.Name == "EntityAttribute";
            }

            // Check for non-generic Entity attribute
            return attrClass.ToDisplayString() == EntityAttributeNonGenericName ||
                   attrClass.Name == "EntityAttribute";
        });

        if (entityAttr is null)
            return null;

        // Always Guid — [Entity] carries no identifier type. See EntityAttribute for why
        // every other answer was worse, and for where a legacy identifier belongs instead.
        const string idType = "System.Guid";

        // Check for other attributes
        var isAuditable = HasAttribute(typeSymbol, AuditableAttributeName);
        var isAudited = HasAttribute(typeSymbol, AuditedAttributeName);
        var isSoftDelete = HasAttribute(typeSymbol, SoftDeleteAttributeName);
        var isSoftDeleteCascade = isSoftDelete && GetSoftDeleteCascade(typeSymbol);
        var isConcurrencyAware = HasAttribute(typeSymbol, ConcurrencyAwareAttributeName);
        var isTenantEntity = ImplementsInterface(typeSymbol, TenantEntityInterfaceName);
        var isOwnedEntity = HasAttribute(typeSymbol, HasOwnerAttributeName);
        var isScopedEntity = HasAttribute(typeSymbol, HasAccessScopesAttributeName);
        var isLookup = HasAttribute(typeSymbol, LookupAttributeName);
        var (inheritanceStrategy, discriminatorColumn) = GetInheritanceInfo(typeSymbol);
        var (stateMachineProperty, initialStateExpression) = GetStateMachineInitialState(typeSymbol);
        var (customTableName, customSchemaName) = GetTableAttribute(typeSymbol);
        var temporalInfo = GetTemporalRelationInfo(typeSymbol);
        var hasGenerateTimeline = HasAttribute(typeSymbol, GenerateTimelineAttributeName);
        var boundaryInfo = GetBoundaryInfo(typeSymbol);

        // Find LogicKey property
        var logicKeys = CollectLogicKeys(typeSymbol);
        var logicKey = logicKeys.Length > 0 ? logicKeys[0].Name : null;

        // Collect properties
        var properties = CollectProperties(typeSymbol, logicKeys, isAuditable, isSoftDelete, isConcurrencyAware, out var ignoredPropertyNames, out var primitiveCollections);
        var allSourceMemberNames = CollectAllPropertyNames(typeSymbol);

        // Detect manual trait properties (PersistenceId, IAuditable, ISoftDelete, IOwnedEntity)
        var manualTraits = DetectManualTraits(typeSymbol);

        // Collect relation attributes and navigations
        var relationAttributes = RelationTransform.CollectRelationAttributes(typeSymbol);
        var usesRelations = relationAttributes.Length > 0;

        // Owned types only (IdentityRecord): relations are declared, and RelationGraphBuilder merges their
        // navigations into these later. Skipped for an entity with relations, this dropped the one thing
        // it collects — a user entity that belonged to a team lost its credentials.
        var navigations = CollectNavigations(typeSymbol);

        // Validate here, not at output time: the rules need the TARGET entity's symbols.
        // Only this source-based transform validates — entities read from a referenced assembly are
        // skipped before generation, so their findings would never be reported.
        // Not gated on usesRelations: a [PartOf] on an entity that declares no relation at all is
        // precisely the case the role rules report, and gating here filtered it out before the
        // validator could see it. The validator returns empty when there is nothing to say.
        var relationDiagnostics = RelationValidator.Validate(typeSymbol);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return new EntityMetadataModel
        {
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(),
            Namespace = ns,
            IdType = idType,
            LogicKeys = logicKeys,
            LogicKeyDeclaredTwice = HasLogicKeyInTwoPlaces(typeSymbol),
            UniqueIndexes = CollectUniqueIndexes(typeSymbol),
            BoundaryName = boundaryInfo.Name,
            BoundaryTypeFullName = boundaryInfo.FullTypeName,
            IsSoftDelete = isSoftDelete,
            IsSoftDeleteCascade = isSoftDeleteCascade,
            IsAuditable = isAuditable,
            IsAudited = isAudited,
            IsDataSubject = HasAttribute(typeSymbol, DataSubjectAttributeName),
            LocalIdentityProperty = LocalIdentityPropertyOf(typeSymbol),
            RegistersFromLocalIdentity = RegistersFromLocalIdentity(typeSymbol),
            IsConcurrencyAware = isConcurrencyAware,
            IsTenantEntity = isTenantEntity,
            IsOwnedEntity = isOwnedEntity,
            VisibilityRules = GetVisibilityRules(typeSymbol),
            IsScopedEntity = isScopedEntity,
            IsTemporalRelation = temporalInfo.IsTemporalRelation,
            TemporalMaxActive = temporalInfo.MaxActive,
            TemporalAllowOverlap = temporalInfo.AllowOverlap,
            TemporalParentTypeName = temporalInfo.ParentTypeName,
            TemporalParentTypeFullName = temporalInfo.ParentTypeFullName,
            TemporalChildTypeName = temporalInfo.ChildTypeName,
            TemporalChildTypeFullName = temporalInfo.ChildTypeFullName,
            TemporalParentFkProperty = temporalInfo.ParentFkProperty,
            TemporalChildFkProperty = temporalInfo.ChildFkProperty,
            HasGenerateTimeline = hasGenerateTimeline,
            CustomTableName = customTableName,
            CustomSchemaName = customSchemaName,
            InheritanceStrategy = inheritanceStrategy,
            DiscriminatorColumn = discriminatorColumn,
            StateMachinePropertyName = stateMachineProperty,
            StateMachineInitialStateExpression = initialStateExpression,
            BaseEntityFullTypeName = GetBaseEntityFullTypeName(typeSymbol),
            Properties = properties,
            IgnoredPropertyNames = ignoredPropertyNames,
            Navigations = navigations,
            PrimitiveCollectionProperties = primitiveCollections,
            RelationAttributes = relationAttributes,
            RelationDiagnostics = relationDiagnostics,
            // Source-based transform only, for the same reason as RelationDiagnostics above.
            PartialTraits = DetectPartialTraits(
                manualTraits.DeclaredTraitProperties, isAuditable, isSoftDelete),
            UsesRelationAttributes = usesRelations,
            AllSourceMemberNames = allSourceMemberNames,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclarationLocation = LocationInfo.From(typeSymbol.Locations.FirstOrDefault()),
            IsAbstract = typeSymbol.IsAbstract,
            IsLookup = isLookup,
            ReadAccessTypes = BoundaryOwnershipReader.ReadAccessTypesOf(typeSymbol),
            HasManualPersistenceId = manualTraits.HasManualPersistenceId,
            HasManualAuditableProps = manualTraits.HasManualAuditableProps,
            HasManualSoftDeleteProps = manualTraits.HasManualSoftDeleteProps,
            HasManualEntityInterface = manualTraits.HasManualEntityInterface,
            HasManualAuditableInterface = manualTraits.HasManualAuditableInterface,
            HasManualSoftDeleteInterface = manualTraits.HasManualSoftDeleteInterface,
            HasManualOwnedEntityProps = manualTraits.HasManualOwnedEntityProps,
            HasManualOwnedEntityInterface = manualTraits.HasManualOwnedEntityInterface,
            HasManualScopedEntityProps = manualTraits.HasManualScopedEntityProps,
            HasManualScopedEntityInterface = manualTraits.HasManualScopedEntityInterface,
            IsValid = true
        };
    }

    /// <summary>
    ///     Transforms a type declaration with [Entity] attribute into EntityMetadataModel.
    ///     (Used with ForAttributeWithMetadataName)
    /// </summary>
    public static EntityMetadataModel? Transform(
        GeneratorAttributeSyntaxContext context,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        // Get the Entity attribute to extract TId
        var entityAttr = context.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.OriginalDefinition.ToDisplayString() == EntityAttributeName ||
            a.AttributeClass?.ToDisplayString() == EntityAttributeNonGenericName);

        if (entityAttr is null)
            return null;

        // Always Guid — [Entity] carries no identifier type. See EntityAttribute for why
        // every other answer was worse, and for where a legacy identifier belongs instead.
        const string idType = "System.Guid";

        // Check for other attributes
        var isAuditable = HasAttribute(typeSymbol, AuditableAttributeName);
        var isAudited = HasAttribute(typeSymbol, AuditedAttributeName);
        var isSoftDelete = HasAttribute(typeSymbol, SoftDeleteAttributeName);
        var isSoftDeleteCascade = isSoftDelete && GetSoftDeleteCascade(typeSymbol);
        var isConcurrencyAware = HasAttribute(typeSymbol, ConcurrencyAwareAttributeName);
        var isTenantEntity = ImplementsInterface(typeSymbol, TenantEntityInterfaceName);
        var isOwnedEntity = HasAttribute(typeSymbol, HasOwnerAttributeName);
        var isScopedEntity = HasAttribute(typeSymbol, HasAccessScopesAttributeName);
        var isLookup = HasAttribute(typeSymbol, LookupAttributeName);
        var (inheritanceStrategy, discriminatorColumn) = GetInheritanceInfo(typeSymbol);
        var (stateMachineProperty, initialStateExpression) = GetStateMachineInitialState(typeSymbol);
        var (customTableName, customSchemaName) = GetTableAttribute(typeSymbol);
        var temporalInfo = GetTemporalRelationInfo(typeSymbol);
        var hasGenerateTimeline = HasAttribute(typeSymbol, GenerateTimelineAttributeName);
        var boundaryInfo = GetBoundaryInfo(typeSymbol);

        // Find LogicKey property
        var logicKeys = CollectLogicKeys(typeSymbol);
        var logicKey = logicKeys.Length > 0 ? logicKeys[0].Name : null;

        // Collect properties
        var properties = CollectProperties(typeSymbol, logicKeys, isAuditable, isSoftDelete, isConcurrencyAware, out var ignoredPropertyNames, out var primitiveCollections);
        var allSourceMemberNames = CollectAllPropertyNames(typeSymbol);

        // Detect manual trait properties (PersistenceId, IAuditable, ISoftDelete, IOwnedEntity)
        var manualTraits = DetectManualTraits(typeSymbol);

        // Collect relation attributes and navigations
        var relationAttributes = RelationTransform.CollectRelationAttributes(typeSymbol);
        var usesRelations = relationAttributes.Length > 0;

        // Owned types only; relation navigations are merged in later.
        var navigations = CollectNavigations(typeSymbol);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return new EntityMetadataModel
        {
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(),
            Namespace = ns,
            IdType = idType,
            LogicKeys = logicKeys,
            LogicKeyDeclaredTwice = HasLogicKeyInTwoPlaces(typeSymbol),
            UniqueIndexes = CollectUniqueIndexes(typeSymbol),
            BoundaryName = boundaryInfo.Name,
            BoundaryTypeFullName = boundaryInfo.FullTypeName,
            IsSoftDelete = isSoftDelete,
            IsSoftDeleteCascade = isSoftDeleteCascade,
            IsAuditable = isAuditable,
            IsAudited = isAudited,
            IsDataSubject = HasAttribute(typeSymbol, DataSubjectAttributeName),
            LocalIdentityProperty = LocalIdentityPropertyOf(typeSymbol),
            RegistersFromLocalIdentity = RegistersFromLocalIdentity(typeSymbol),
            IsConcurrencyAware = isConcurrencyAware,
            IsTenantEntity = isTenantEntity,
            IsOwnedEntity = isOwnedEntity,
            VisibilityRules = GetVisibilityRules(typeSymbol),
            IsScopedEntity = isScopedEntity,
            IsTemporalRelation = temporalInfo.IsTemporalRelation,
            TemporalMaxActive = temporalInfo.MaxActive,
            TemporalAllowOverlap = temporalInfo.AllowOverlap,
            TemporalParentTypeName = temporalInfo.ParentTypeName,
            TemporalParentTypeFullName = temporalInfo.ParentTypeFullName,
            TemporalChildTypeName = temporalInfo.ChildTypeName,
            TemporalChildTypeFullName = temporalInfo.ChildTypeFullName,
            TemporalParentFkProperty = temporalInfo.ParentFkProperty,
            TemporalChildFkProperty = temporalInfo.ChildFkProperty,
            HasGenerateTimeline = hasGenerateTimeline,
            CustomTableName = customTableName,
            CustomSchemaName = customSchemaName,
            InheritanceStrategy = inheritanceStrategy,
            DiscriminatorColumn = discriminatorColumn,
            StateMachinePropertyName = stateMachineProperty,
            StateMachineInitialStateExpression = initialStateExpression,
            BaseEntityFullTypeName = GetBaseEntityFullTypeName(typeSymbol),
            Properties = properties,
            IgnoredPropertyNames = ignoredPropertyNames,
            Navigations = navigations,
            PrimitiveCollectionProperties = primitiveCollections,
            RelationAttributes = relationAttributes,
            UsesRelationAttributes = usesRelations,
            AllSourceMemberNames = allSourceMemberNames,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclarationLocation = LocationInfo.From(typeSymbol.Locations.FirstOrDefault()),
            IsAbstract = typeSymbol.IsAbstract,
            IsLookup = isLookup,
            ReadAccessTypes = BoundaryOwnershipReader.ReadAccessTypesOf(typeSymbol),
            HasManualPersistenceId = manualTraits.HasManualPersistenceId,
            HasManualAuditableProps = manualTraits.HasManualAuditableProps,
            HasManualSoftDeleteProps = manualTraits.HasManualSoftDeleteProps,
            HasManualEntityInterface = manualTraits.HasManualEntityInterface,
            HasManualAuditableInterface = manualTraits.HasManualAuditableInterface,
            HasManualSoftDeleteInterface = manualTraits.HasManualSoftDeleteInterface,
            HasManualOwnedEntityProps = manualTraits.HasManualOwnedEntityProps,
            HasManualOwnedEntityInterface = manualTraits.HasManualOwnedEntityInterface,
            HasManualScopedEntityProps = manualTraits.HasManualScopedEntityProps,
            HasManualScopedEntityInterface = manualTraits.HasManualScopedEntityInterface,
            IsValid = true
        };
    }

    /// <summary>
    ///     Transforms an <see cref="INamedTypeSymbol"/> into EntityMetadataModel if it has [Entity] attribute.
    ///     Used for scanning entities from referenced assemblies during host-mode cross-assembly discovery.
    /// </summary>
    public static EntityMetadataModel? TransformFromSymbol(
        INamedTypeSymbol typeSymbol,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Look for Entity attribute
        var entityAttr = typeSymbol.GetAttributes().FirstOrDefault(a =>
        {
            var attrClass = a.AttributeClass;
            if (attrClass is null)
                return false;

            if (attrClass.IsGenericType)
            {
                var def = attrClass.OriginalDefinition;
                return def.ToDisplayString() == EntityAttributeName || def.Name == "EntityAttribute";
            }

            return attrClass.ToDisplayString() == EntityAttributeNonGenericName ||
                   attrClass.Name == "EntityAttribute";
        });

        if (entityAttr is null)
            return null;

        // Always Guid — [Entity] carries no identifier type. See EntityAttribute for why
        // every other answer was worse, and for where a legacy identifier belongs instead.
        const string idType = "System.Guid";

        // Check for other attributes
        var isAuditable = HasAttribute(typeSymbol, AuditableAttributeName);
        var isAudited = HasAttribute(typeSymbol, AuditedAttributeName);
        var isSoftDelete = HasAttribute(typeSymbol, SoftDeleteAttributeName);
        var isSoftDeleteCascade = isSoftDelete && GetSoftDeleteCascade(typeSymbol);
        var isConcurrencyAware = HasAttribute(typeSymbol, ConcurrencyAwareAttributeName);
        var isTenantEntity = ImplementsInterface(typeSymbol, TenantEntityInterfaceName);
        // Ownership/scoping: attribute OR interface (interface is SG-generated and visible in referenced assemblies)
        var isOwnedEntity = HasAttribute(typeSymbol, HasOwnerAttributeName) ||
                            ImplementsInterface(typeSymbol, OwnedEntityInterfaceName);
        var isScopedEntity = HasAttribute(typeSymbol, HasAccessScopesAttributeName) ||
                             ImplementsInterface(typeSymbol, ScopedEntityInterfaceName);
        var isLookup = HasAttribute(typeSymbol, LookupAttributeName);
        var (inheritanceStrategy, discriminatorColumn) = GetInheritanceInfo(typeSymbol);
        var (stateMachineProperty, initialStateExpression) = GetStateMachineInitialState(typeSymbol);
        var (customTableName, customSchemaName) = GetTableAttribute(typeSymbol);
        var temporalInfo = GetTemporalRelationInfo(typeSymbol);
        var hasGenerateTimeline = HasAttribute(typeSymbol, GenerateTimelineAttributeName);
        var boundaryInfo = GetBoundaryInfo(typeSymbol);

        // Find LogicKey property
        var logicKeys = CollectLogicKeys(typeSymbol);
        var logicKey = logicKeys.Length > 0 ? logicKeys[0].Name : null;

        // Collect properties
        var properties = CollectProperties(typeSymbol, logicKeys, isAuditable, isSoftDelete, isConcurrencyAware, out var ignoredPropertyNames, out var primitiveCollections);
        var allSourceMemberNames = CollectAllPropertyNames(typeSymbol);

        // Detect manual trait properties (PersistenceId, IAuditable, ISoftDelete, IOwnedEntity)
        var manualTraits = DetectManualTraits(typeSymbol);

        // Collect relation attributes and navigations
        var relationAttributes = RelationTransform.CollectRelationAttributes(typeSymbol);
        var usesRelations = relationAttributes.Length > 0;

        // Owned types only; relation navigations are merged in later.
        var navigations = CollectNavigations(typeSymbol);

        var ns = typeSymbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : typeSymbol.ContainingNamespace.ToDisplayString();

        return new EntityMetadataModel
        {
            TypeName = typeSymbol.Name,
            FullTypeName = typeSymbol.ToDisplayString(),
            Namespace = ns,
            IdType = idType,
            LogicKeys = logicKeys,
            LogicKeyDeclaredTwice = HasLogicKeyInTwoPlaces(typeSymbol),
            UniqueIndexes = CollectUniqueIndexes(typeSymbol),
            BoundaryName = boundaryInfo.Name,
            BoundaryTypeFullName = boundaryInfo.FullTypeName,
            IsSoftDelete = isSoftDelete,
            IsSoftDeleteCascade = isSoftDeleteCascade,
            IsAuditable = isAuditable,
            IsAudited = isAudited,
            IsDataSubject = HasAttribute(typeSymbol, DataSubjectAttributeName),
            LocalIdentityProperty = LocalIdentityPropertyOf(typeSymbol),
            RegistersFromLocalIdentity = RegistersFromLocalIdentity(typeSymbol),
            IsConcurrencyAware = isConcurrencyAware,
            IsTenantEntity = isTenantEntity,
            IsOwnedEntity = isOwnedEntity,
            VisibilityRules = GetVisibilityRules(typeSymbol),
            IsScopedEntity = isScopedEntity,
            IsTemporalRelation = temporalInfo.IsTemporalRelation,
            TemporalMaxActive = temporalInfo.MaxActive,
            TemporalAllowOverlap = temporalInfo.AllowOverlap,
            TemporalParentTypeName = temporalInfo.ParentTypeName,
            TemporalParentTypeFullName = temporalInfo.ParentTypeFullName,
            TemporalChildTypeName = temporalInfo.ChildTypeName,
            TemporalChildTypeFullName = temporalInfo.ChildTypeFullName,
            TemporalParentFkProperty = temporalInfo.ParentFkProperty,
            TemporalChildFkProperty = temporalInfo.ChildFkProperty,
            HasGenerateTimeline = hasGenerateTimeline,
            CustomTableName = customTableName,
            CustomSchemaName = customSchemaName,
            InheritanceStrategy = inheritanceStrategy,
            DiscriminatorColumn = discriminatorColumn,
            StateMachinePropertyName = stateMachineProperty,
            StateMachineInitialStateExpression = initialStateExpression,
            BaseEntityFullTypeName = GetBaseEntityFullTypeName(typeSymbol),
            Properties = properties,
            IgnoredPropertyNames = ignoredPropertyNames,
            Navigations = navigations,
            PrimitiveCollectionProperties = primitiveCollections,
            RelationAttributes = relationAttributes,
            UsesRelationAttributes = usesRelations,
            AllSourceMemberNames = allSourceMemberNames,
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            DeclarationLocation = LocationInfo.From(typeSymbol.Locations.FirstOrDefault()),
            IsAbstract = typeSymbol.IsAbstract,
            IsLookup = isLookup,
            ReadAccessTypes = BoundaryOwnershipReader.ReadAccessTypesOf(typeSymbol),
            HasManualPersistenceId = manualTraits.HasManualPersistenceId,
            HasManualAuditableProps = manualTraits.HasManualAuditableProps,
            HasManualSoftDeleteProps = manualTraits.HasManualSoftDeleteProps,
            HasManualEntityInterface = manualTraits.HasManualEntityInterface,
            HasManualAuditableInterface = manualTraits.HasManualAuditableInterface,
            HasManualSoftDeleteInterface = manualTraits.HasManualSoftDeleteInterface,
            HasManualOwnedEntityProps = manualTraits.HasManualOwnedEntityProps,
            HasManualOwnedEntityInterface = manualTraits.HasManualOwnedEntityInterface,
            HasManualScopedEntityProps = manualTraits.HasManualScopedEntityProps,
            HasManualScopedEntityInterface = manualTraits.HasManualScopedEntityInterface,
            IsValid = true
        };
    }

    // =========================================================================
    // Attribute parsing → EntityTransform.Attributes.cs
    // Property collection → EntityTransform.Properties.cs
    // =========================================================================
}

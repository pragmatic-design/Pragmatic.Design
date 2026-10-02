using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents entity metadata read from assembly attributes.
///     Used to generate EntityConfiguration in the persistence layer.
///     Custom equality implemented to support Roslyn incremental caching —
///     <see cref="ImmutableArray{T}"/> fields use sequence equality instead of reference equality.
/// </summary>
internal sealed record EntityMetadataModel : IEquatable<EntityMetadataModel>
{
    /// <summary>
    ///     The entity type name (e.g., "Order").
    /// </summary>
    public required string TypeName { get; init; }

    /// <summary>
    ///     The full qualified entity type name (e.g., "Contoso.Sales.Entities.Order").
    /// </summary>
    public required string FullTypeName { get; init; }

    /// <summary>
    ///     The entity namespace.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The ID type (e.g., "Guid", "int", "long").
    /// </summary>
    public required string IdType { get; init; }

    /// <summary>
    ///     The properties that together form the entity's domain key, in declaration order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The logic key <b>is</b> the domain key; the persistence key is a simplification of it. A
    ///         domain key is very often more than one column — a room type is unique per property, not
    ///         globally — and modelling it as a single property made every such entity either lie about
    ///         its uniqueness or enforce it by hand.
    ///     </para>
    ///     <para>
    ///         With exactly one part, everything downstream emits what it always emitted: the same
    ///         single-column index, the same <c>GetBy{Name}Async</c>, the same route. Nothing about an
    ///         existing schema moves until a second <c>[LogicKey]</c> is declared.
    ///     </para>
    /// </remarks>
    public EquatableArray<LogicKeyPart> LogicKeys { get; init; } = EquatableArray<LogicKeyPart>.Empty;

    /// <summary>
    ///     Whether <c>[LogicKey]</c> sits both on the class and on a property: <c>PRAG0637</c>. Read
    ///     in the module only — the class form is what <see cref="LogicKeys" /> holds when both exist.
    /// </summary>
    public bool LogicKeyDeclaredTwice { get; init; }

    /// <summary>
    ///     The unique indexes declared with <c>[Unique]</c>, beyond the domain key.
    /// </summary>
    /// <remarks>
    ///     Without it <c>[LogicKey]</c> would be the only way to declare a unique index, so an entity
    ///     could declare exactly one: a second constraint would have to be written by hand in
    ///     <c>OnModelCreating</c>, where the migrations never see it, or go unenforced.
    /// </remarks>
    public EquatableArray<UniqueIndexModel> UniqueIndexes { get; init; } = EquatableArray<UniqueIndexModel>.Empty;

    /// <summary>
    ///     The first logic key property name, or null. Kept because most consumers address a
    ///     single-property key and read better for it; <see cref="LogicKeys"/> is the whole truth.
    /// </summary>
    public string? LogicKey => LogicKeys.Length > 0 ? LogicKeys[0].Name : null;

    /// <summary>Whether the domain key spans more than one property.</summary>
    public bool HasCompositeLogicKey => LogicKeys.Length > 1;

    /// <summary>
    ///     The lambda that selects the domain key for <c>HasIndex</c>, in the order the parts asked
    ///     for — declaration order unless <c>[LogicKey(Order = n)]</c> says otherwise.
    /// </summary>
    /// <remarks>
    ///     One part keeps the single-column form — <c>e => e.Code</c> — which is what every entity in
    ///     existence emits today, so no index changes shape until a second <c>[LogicKey]</c> appears.
    ///     Written here rather than in each template because two templates emit this index and a
    ///     disagreement between them is a second index, not a compile error.
    /// </remarks>
    public string LogicKeySelector
    {
        get
        {
            if (LogicKeys.Length == 0)
                return "e => e.PersistenceId";

            var names = LogicKeyColumns;

            if (names.Length == 1)
                return $"e => e.{names[0]}";

            return $"e => new {{ {string.Join(", ", names.Select(n => $"e.{n}"))} }}";
        }
    }

    /// <summary>
    ///     The columns of the domain key's unique index, in order — the parts as declared, led by
    ///     <c>TenantId</c> when the key is unique within the tenant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <b>The tenant is part of the index.</b> Over the marked columns alone, a
    ///         <c>[LogicKey]</c> on an <c>ITenantEntity</c> would make uniqueness reach across every
    ///         tenant: one workspace taking a value would take it from all of them, with a conflict that
    ///         names nothing and tells the loser somebody already holds it.
    ///     </para>
    ///     <para>
    ///         Leading rather than trailing: a composite index can only be searched by its leading
    ///         columns, and every query against a tenant-scoped table is filtered by tenant first.
    ///     </para>
    ///     <para>
    ///         Written here because three places emit this index — the entity configuration, the
    ///         DbContext's filtered variant, and the schema the migrations build from — and a
    ///         disagreement between them is a second index, not a compile error.
    ///     </para>
    /// </remarks>
    public ImmutableArray<string> LogicKeyColumns
    {
        get
        {
            if (LogicKeys.Length == 0)
                return ImmutableArray<string>.Empty;

            var declared = LogicKeys.AsImmutableArray().Select(p => p.Name);

            return LogicKeyIsPerTenant
                ? ImmutableArray.CreateRange(new[] { "TenantId" }.Concat(declared))
                : ImmutableArray.CreateRange(declared);
        }
    }

    /// <summary>
    ///     Whether the domain key is unique within the tenant rather than across all of them.
    /// </summary>
    /// <remarks>
    ///     True only where the question arises: the entity is tenant-scoped, it has a domain key, and
    ///     no part of that key asked for <c>UniquenessScope.Global</c>. Parts that disagree are
    ///     <c>PRAG0625</c>; this reads them as global so the diagnostic is what the author sees rather
    ///     than a schema change they did not ask for.
    /// </remarks>
    public bool LogicKeyIsPerTenant
        => IsTenantEntity
           && LogicKeys.Length > 0
           && !LogicKeys.AsImmutableArray().Any(p => p.IsGlobal);

    /// <summary>
    ///     The columns of one <c>[Unique]</c> index, in order — as written, led by <c>TenantId</c>
    ///     unless the index asked to be global.
    /// </summary>
    /// <remarks>
    ///     The same rule as <see cref="LogicKeyColumns" /> and deliberately in the same file: two
    ///     kinds of unique index that answered the tenant question differently would be a trap, since
    ///     nothing on the page would say which kind you were looking at.
    /// </remarks>
    public ImmutableArray<string> UniqueIndexColumns(UniqueIndexModel index)
    {
        var declared = index.Columns.AsImmutableArray();

        return IsTenantEntity && !index.IsGlobal
            ? ImmutableArray.CreateRange(new[] { "TenantId" }.Concat(declared))
            : declared;
    }

    /// <summary>
    ///     The boundary name this entity belongs to.
    /// </summary>
    public string? BoundaryName { get; init; }

    /// <summary>
    ///     The fully qualified type name of the boundary marker class
    ///     (e.g., "Contoso.Sales.Actions.SalesBoundary").
    /// </summary>
    public string? BoundaryTypeFullName { get; init; }

    /// <summary>
    ///     The entities this entity's boundary reads with <c>[ReadAccess&lt;T&gt;]</c>. A relation to one
    ///     of them generates its navigation: the target is in this boundary's DbContext, read-only.
    /// </summary>
    public EquatableArray<string> ReadAccessTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether the entity implements ISoftDelete.
    /// </summary>
    public bool IsSoftDelete { get; init; }

    /// <summary>
    ///     Whether soft-delete cascades to related entities via navigation properties.
    /// </summary>
    public bool IsSoftDeleteCascade { get; init; }

    /// <summary>
    ///     Whether the entity implements IAuditable.
    /// </summary>
    public bool IsAuditable { get; init; }

    /// <summary>
    ///     Whether the entity has [Audited] — emits IAuditedEntity + an append-only audit log row per change.
    /// </summary>
    public bool IsAudited { get; init; }

    /// <summary>
    ///     Whether the entity is a <c>[DataSubject]</c> — the boundary that holds it maps the subject
    ///     registry's tables when the registry package is referenced.
    /// </summary>
    public bool IsDataSubject { get; init; }

    /// <summary>
    ///     Whether the entity has [ConcurrencyAware] attribute (optimistic concurrency).
    /// </summary>
    public bool IsConcurrencyAware { get; init; }

    /// <summary>
    ///     Whether the entity implements ITenantEntity (row-level tenant isolation).
    /// </summary>
    public bool IsTenantEntity { get; init; }

    /// <summary>
    ///     Whether the entity has [HasOwner] attribute (creator ownership).
    /// </summary>
    public bool IsOwnedEntity { get; init; }

    /// <summary>
    ///     Whether the entity has [HasAccessScopes] attribute (scope-based access control).
    /// </summary>
    public bool IsScopedEntity { get; init; }

    /// <summary>
    ///     Whether the entity has [TemporalRelation] attribute and implements ITemporalRelation.
    /// </summary>
    public bool IsTemporalRelation { get; init; }

    /// <summary>
    ///     Maximum number of active temporal relations allowed (0 = no limit).
    /// </summary>
    public int TemporalMaxActive { get; init; }

    /// <summary>
    ///     Whether overlapping validity periods are allowed for this temporal relation.
    /// </summary>
    public bool TemporalAllowOverlap { get; init; }

    /// <summary>
    ///     Parent type name from [TemporalRelation&lt;TParent, TChild&gt;].
    ///     Null when using the non-generic [TemporalRelation].
    /// </summary>
    public string? TemporalParentTypeName { get; init; }

    /// <summary>
    ///     Parent type full name from [TemporalRelation&lt;TParent, TChild&gt;].
    /// </summary>
    public string? TemporalParentTypeFullName { get; init; }

    /// <summary>
    ///     Child type name from [TemporalRelation&lt;TParent, TChild&gt;].
    ///     Null when using the non-generic [TemporalRelation].
    /// </summary>
    public string? TemporalChildTypeName { get; init; }

    /// <summary>
    ///     Child type full name from [TemporalRelation&lt;TParent, TChild&gt;].
    /// </summary>
    public string? TemporalChildTypeFullName { get; init; }

    /// <summary>
    ///     FK property name for the parent (e.g. "PropertyId"). Convention: {TParent.Name}Id.
    /// </summary>
    public string? TemporalParentFkProperty { get; init; }

    /// <summary>
    ///     FK property name for the child (e.g. "StaffId"). Convention: {TChild.Name}Id.
    /// </summary>
    public string? TemporalChildFkProperty { get; init; }

    /// <summary>
    ///     Whether this is a typed temporal relation (has parent/child type info).
    /// </summary>
    public bool IsTypedTemporalRelation => TemporalParentTypeName is not null;

    /// <summary>
    ///     Whether this entity has [GenerateTimeline] for CTE timeline queries.
    /// </summary>
    public bool HasGenerateTimeline { get; init; }

    /// <summary>
    ///     Whether this entity class is declared as abstract.
    /// </summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    ///     Whether this entity is a lookup table ([Lookup] attribute).
    /// </summary>
    public bool IsLookup { get; init; }

    /// <summary>
    ///     Custom table name from [Table("name")] attribute, or null to use Pluralize(TypeName).
    /// </summary>
    public string? CustomTableName { get; init; }

    /// <summary>
    ///     Custom schema name from [Table("name", Schema="schema")] attribute.
    /// </summary>
    public string? CustomSchemaName { get; init; }

    /// <summary>
    ///     The inheritance mapping strategy (TPH, TPT, TPC), if specified.
    /// </summary>
    public string? InheritanceStrategy { get; init; }

    /// <summary>
    ///     The fully qualified base type name when this entity derives from another entity
    ///     (e.g., "Showcase.Billing.Entities.Fee" for ServiceFee).
    ///     Used by SchemaMetadataTransform to merge TPH derived columns into the base table.
    /// </summary>
    public string? BaseEntityFullTypeName { get; init; }

    /// <summary>
    ///     Custom discriminator column name for TPH (default: "Discriminator").
    ///     Only set when <see cref="InheritanceStrategy"/> is "TPH".
    /// </summary>
    public string? DiscriminatorColumn { get; init; }

    /// <summary>
    ///     The state-machine property this entity is governed by, when it declares
    ///     <c>[StateMachine&lt;TEnum&gt;]</c>. Defaults to <c>Status</c> in the attribute.
    /// </summary>
    public string? StateMachinePropertyName { get; init; }

    /// <summary>
    ///     The fully qualified expression naming the enum value marked <c>[InitialState]</c> — e.g.
    ///     <c>global::Showcase.Booking.Enums.ReservationStatus.Pending</c>.
    /// </summary>
    /// <remarks>
    ///     Read here, on the entity pipeline, because the factory that has to assign it is generated
    ///     here. Before this the initial state was computed by <c>StateMachineTransform</c>, validated
    ///     by <c>StateMachineValidator</c> and read by nobody: <c>[InitialState]</c> emitted no code at
    ///     all, and a new entity took the enum's numeric zero. Both reference applications marked the
    ///     first declared value, so zero and the intended state coincided and nothing looked wrong.
    /// </remarks>
    public string? StateMachineInitialStateExpression { get; init; }

    /// <summary>
    ///     Properties of this entity.
    /// </summary>
    public EquatableArray<PropertyMetadataModel> Properties { get; init; } =
        EquatableArray<PropertyMetadataModel>.Empty;

    /// <summary>
    ///     Navigation relationships of this entity.
    /// </summary>
    public EquatableArray<NavigationMetadataModel> Navigations { get; init; } =
        EquatableArray<NavigationMetadataModel>.Empty;

    /// <summary>
    ///     Collection-of-primitives properties (List&lt;string&gt;, string[], …) that EF Core
    ///     persists as JSON primitive collections rather than navigations.
    /// </summary>
    public EquatableArray<PrimitiveCollectionMetadataModel> PrimitiveCollectionProperties { get; init; } =
        EquatableArray<PrimitiveCollectionMetadataModel>.Empty;

    /// <summary>
    ///     Relation attributes declared on this entity ([Relation.OneToMany], etc.).
    /// </summary>
    public EquatableArray<RelationAttributeModel> RelationAttributes { get; init; } =
        EquatableArray<RelationAttributeModel>.Empty;

    /// <summary>
    ///     Whether this entity uses explicit [Relation.*] attributes for navigation declaration.
    ///     When true, navigation properties are generated by the SG instead of declared manually.
    /// </summary>
    public bool UsesRelationAttributes { get; init; }

    /// <summary>
    ///     [Relation.*] rule violations found while reading this entity (PRAG0612-PRAG0615).
    ///     Validation needs symbols, reporting happens in the source-output stage, so the findings
    ///     travel through the pipeline as cache-safe values.
    /// </summary>
    public EquatableArray<RelationDiagnosticModel> RelationDiagnostics { get; init; } =
        EquatableArray<RelationDiagnosticModel>.Empty;

    /// <summary>
    ///     Trait property groups this entity declares only part of (PRAG0624). Populated by the
    ///     source-based transform only: an entity read from a referenced assembly was already compiled,
    ///     and reporting it here would blame a file in someone else's project.
    /// </summary>
    public EquatableArray<PartialTraitModel> PartialTraits { get; init; } =
        EquatableArray<PartialTraitModel>.Empty;

    /// <summary>
    ///     All property names declared in source code (including those filtered out as navigations).
    ///     Used by RelationGraphBuilder for deduplication — prevents generating properties that already exist in source.
    /// </summary>
    /// <summary>
    ///     The <c>VisibilityRule&lt;T&gt;</c> types declared with <c>[VisibleWhen&lt;TRule&gt;]</c>,
    ///     fully qualified. Each is registered as an <c>IQueryFilter</c>, so it applies to every query
    ///     of the entity.
    /// </summary>
    /// <remarks>
    ///     The rule is the filter — nothing is generated to wrap it — so this carries the author's own
    ///     type name, which is also the name they write in <c>Disable&lt;TRule&gt;()</c>.
    /// </remarks>
    public EquatableArray<string> VisibilityRules { get; init; } = EquatableArray<string>.Empty;

    public EquatableArray<string> AllSourceMemberNames { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Properties to generate in the {Entity}.Relations.g.cs file.
    ///     Populated by RelationGraphBuilder after cross-entity wiring.
    /// </summary>
    public EquatableArray<GeneratedRelationPropertyModel> GeneratedRelationProperties { get; init; } =
        EquatableArray<GeneratedRelationPropertyModel>.Empty;

    /// <summary>
    ///     Whether this entity has any generated relation properties.
    /// </summary>
    public bool HasGeneratedRelationProperties => GeneratedRelationProperties.Length > 0;

    /// <summary>
    ///     Whether this entity was read from a referenced assembly (true)
    ///     or discovered via syntax analysis in the current compilation (false).
    ///     Used to determine host mode: DbContexts are only generated when all
    ///     boundary entities come from references (the current project is the host).
    /// </summary>
    public bool IsFromReference { get; init; } = true;

    /// <summary>
    ///     True when the entity belongs to an imported package rather than to a module of this
    ///     application.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It is still a full entity of the boundary that adopted it — same DbContext, same schema.
    ///     What it is not is <b>representative</b>: the namespace of the application's generated
    ///     artifacts is read off an entity, and <c>Pragmatic.Authorization.Management.Entities</c>
    ///     sorts before <c>Showcase.*</c>. The schema class moved to another namespace and the host
    ///     stopped compiling.
    /// </remarks>
    public bool IsFromImportedPackage { get; init; }

    /// <summary>
    ///     The entity carries a generated <c>ParentVisibilityFilter</c> — a trait child restricted to
    ///     the rows whose parent the caller may see.
    /// </summary>
    public bool HasParentVisibilityFilter { get; init; }

    /// <summary>
    ///     The entity carries a generated <c>InternalVisibilityFilter</c> — a comment trait whose rows
    ///     marked Internal are read only with the <c>comments.view-internal</c> permission.
    /// </summary>
    public bool HasInternalVisibilityFilter { get; init; }

    /// <summary>
    ///     On a <c>[PragmaticUser]</c>, the property holding its <c>LocalIdentity</c> — the credentials the
    ///     generated <c>{Entity}.LocalIdentityStore</c> reads and writes. <c>null</c> for every other entity.
    /// </summary>
    public string? LocalIdentityProperty { get; init; }

    /// <summary>
    ///     The user entity implements <c>ISelfRegisteringUser&lt;TSelf&gt;</c>: the store's
    ///     <c>CreateAsync</c> builds one through it. Without it the application creates its users itself,
    ///     and registration is refused.
    /// </summary>
    public bool RegistersFromLocalIdentity { get; init; }

    /// <summary>
    ///     The reference navigation to a tenant-scoped parent, for a trait child that has no tenant
    ///     column of its own. <c>null</c> for every other entity.
    /// </summary>
    /// <remarks>
    ///     The DbContext lifts the parent's tenant query filter onto the child through it. Without it
    ///     the child's generated list query filters by the parent id and nothing else, so a caller
    ///     holding an id from another tenant reads the whole collection.
    /// </remarks>
    public string? ParentTenantNavigation { get; init; }

    /// <summary>
    ///     Whether this model is valid for code generation.
    /// </summary>
    public bool IsValid { get; init; } = true;

    /// <summary>
    ///     Whether this entity has any properties.
    /// </summary>
    public bool HasProperties => Properties.Length > 0;

    /// <summary>
    ///     Whether this entity has any navigation properties.
    /// </summary>
    public bool HasNavigations => Navigations.Length > 0;

    /// <summary>
    ///     Whether this entity has any properties with private setters.
    /// </summary>
    public bool HasPrivateSetterProperties => Properties.Any(p => p.HasPrivateSetter);

    /// <summary>
    ///     Gets properties with private setters.
    /// </summary>
    public IEnumerable<PropertyMetadataModel> PrivateSetterProperties =>
        Properties.Where(p => p.HasPrivateSetter);

    /// <summary>
    ///     Property names to explicitly ignore in EntityConfiguration.
    ///     These are infrastructure properties (e.g., DomainEvents, ModifiedProperties)
    ///     that EF Core would try to map but are not database columns.
    /// </summary>
    public EquatableArray<string> IgnoredPropertyNames { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Explicit primary-key column names. Empty — the default for every entity modelled the
    ///     Pragmatic way — means the table carries the surrogate <c>PersistenceId</c> key.
    ///     A non-empty value replaces that surrogate with the listed columns, which is what a
    ///     junction entity keyed on its two foreign keys needs: EF has no surrogate to write, so a
    ///     mandatory <c>PersistenceId</c> column would make every insert fail.
    /// </summary>
    public EquatableArray<string> KeyColumns { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Columns forming a unique constraint spanning more than one column. <c>[LogicKey]</c> covers
    ///     the single-column case; this covers a composite one, such as the shared tag's
    ///     (<c>Value</c>, <c>Scope</c>) pair, whose uniqueness is what makes tag de-duplication work.
    /// </summary>
    public EquatableArray<string> UniqueColumns { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Position of the entity declaration, when it comes from source. Null for entities read from a
    ///     referenced assembly (metadata symbols have no syntax tree) — those are never the subject of a
    ///     diagnostic anyway, since diagnostics fire in the project that owns the declaration.
    ///     Deliberately excluded from <see cref="Equals(EntityMetadataModel)"/>: a position never changes
    ///     the generated output (see <see cref="LocationInfo"/>).
    /// </summary>
    public LocationInfo? DeclarationLocation { get; init; }

    /// <summary>
    ///     The accessibility modifier (public, internal, etc.).
    /// </summary>
    public string Accessibility { get; init; } = "public";

    /// <summary>
    ///     The first logic key property's type, or null. See <see cref="LogicKey"/>.
    /// </summary>
    public string? LogicKeyType => LogicKeys.Length > 0 ? LogicKeys[0].TypeName : null;

    // =========================================================================
    // Manual trait detection flags — used by EntityTraitsTemplate to skip
    // generating properties that the developer already declares manually.
    // =========================================================================

    /// <summary>
    ///     Whether the entity already declares PersistenceId (and Id) in source code.
    /// </summary>
    public bool HasManualPersistenceId { get; init; }

    /// <summary>
    ///     Whether the entity already declares IAuditable properties (CreatedAt, CreatedBy, UpdatedAt, UpdatedBy).
    /// </summary>
    public bool HasManualAuditableProps { get; init; }

    /// <summary>
    ///     Whether the entity already declares ISoftDelete properties (IsDeleted, DeletedAt, DeletedBy).
    /// </summary>
    public bool HasManualSoftDeleteProps { get; init; }

    /// <summary>
    ///     Whether the entity already implements IEntity in source code (explicit or via base class).
    /// </summary>
    public bool HasManualEntityInterface { get; init; }

    /// <summary>
    ///     Whether the entity already implements IAuditable in source code (explicit or via base class).
    /// </summary>
    public bool HasManualAuditableInterface { get; init; }

    /// <summary>
    ///     Whether the entity already implements ISoftDelete in source code (explicit or via base class).
    /// </summary>
    public bool HasManualSoftDeleteInterface { get; init; }

    /// <summary>
    ///     Whether the entity already declares OwnerId property in source code.
    /// </summary>
    public bool HasManualOwnedEntityProps { get; init; }

    /// <summary>
    ///     Whether the entity already implements IOwnedEntity in source code.
    /// </summary>
    public bool HasManualOwnedEntityInterface { get; init; }

    /// <summary>
    ///     Whether the entity already declares AccessScopes property in source code.
    /// </summary>
    public bool HasManualScopedEntityProps { get; init; }

    /// <summary>
    ///     Whether the entity already implements IScopedEntity in source code.
    /// </summary>
    public bool HasManualScopedEntityInterface { get; init; }

    /// <summary>
    ///     Whether the entity needs any auto-generated trait properties
    ///     (i.e., it has [Auditable] or [SoftDelete] but is missing some boilerplate).
    /// </summary>
    public bool NeedsTraitGeneration =>
        (!HasManualPersistenceId) ||
        (IsAuditable && !HasManualAuditableProps) ||
        (IsSoftDelete && !HasManualSoftDeleteProps) ||
        (IsOwnedEntity && !HasManualOwnedEntityProps) ||
        (IsScopedEntity && !HasManualScopedEntityProps);

    // =========================================================================
    // Custom equality for Roslyn incremental caching.
    // EquatableArray<T> uses reference equality by default, which breaks caching.
    // =========================================================================

    public bool Equals(EntityMetadataModel? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return TypeName == other.TypeName &&
               FullTypeName == other.FullTypeName &&
               Namespace == other.Namespace &&
               IdType == other.IdType &&
               LogicKeys.Equals(other.LogicKeys) &&
               LogicKeyDeclaredTwice == other.LogicKeyDeclaredTwice &&
               UniqueIndexes.Equals(other.UniqueIndexes) &&
               BoundaryName == other.BoundaryName &&
               BoundaryTypeFullName == other.BoundaryTypeFullName &&
               SequenceEqual(ReadAccessTypes, other.ReadAccessTypes) &&
               IsSoftDelete == other.IsSoftDelete &&
               IsSoftDeleteCascade == other.IsSoftDeleteCascade &&
               IsAuditable == other.IsAuditable &&
               IsAudited == other.IsAudited &&
               IsDataSubject == other.IsDataSubject &&
               IsConcurrencyAware == other.IsConcurrencyAware &&
               IsTenantEntity == other.IsTenantEntity &&
               IsOwnedEntity == other.IsOwnedEntity &&
               IsScopedEntity == other.IsScopedEntity &&
               IsTemporalRelation == other.IsTemporalRelation &&
               TemporalMaxActive == other.TemporalMaxActive &&
               TemporalAllowOverlap == other.TemporalAllowOverlap &&
               TemporalParentTypeName == other.TemporalParentTypeName &&
               TemporalParentTypeFullName == other.TemporalParentTypeFullName &&
               TemporalChildTypeName == other.TemporalChildTypeName &&
               TemporalChildTypeFullName == other.TemporalChildTypeFullName &&
               TemporalParentFkProperty == other.TemporalParentFkProperty &&
               TemporalChildFkProperty == other.TemporalChildFkProperty &&
               HasGenerateTimeline == other.HasGenerateTimeline &&
               IsAbstract == other.IsAbstract &&
               IsLookup == other.IsLookup &&
               CustomTableName == other.CustomTableName &&
               CustomSchemaName == other.CustomSchemaName &&
               InheritanceStrategy == other.InheritanceStrategy &&
               BaseEntityFullTypeName == other.BaseEntityFullTypeName &&
               DiscriminatorColumn == other.DiscriminatorColumn &&
               StateMachinePropertyName == other.StateMachinePropertyName &&
               StateMachineInitialStateExpression == other.StateMachineInitialStateExpression &&
               UsesRelationAttributes == other.UsesRelationAttributes &&
               IsFromReference == other.IsFromReference &&
               HasParentVisibilityFilter == other.HasParentVisibilityFilter &&
               HasInternalVisibilityFilter == other.HasInternalVisibilityFilter &&
               ParentTenantNavigation == other.ParentTenantNavigation &&
               IsValid == other.IsValid &&
               Accessibility == other.Accessibility &&
               HasManualPersistenceId == other.HasManualPersistenceId &&
               HasManualAuditableProps == other.HasManualAuditableProps &&
               HasManualSoftDeleteProps == other.HasManualSoftDeleteProps &&
               HasManualEntityInterface == other.HasManualEntityInterface &&
               HasManualAuditableInterface == other.HasManualAuditableInterface &&
               HasManualSoftDeleteInterface == other.HasManualSoftDeleteInterface &&
               HasManualOwnedEntityProps == other.HasManualOwnedEntityProps &&
               HasManualOwnedEntityInterface == other.HasManualOwnedEntityInterface &&
               HasManualScopedEntityProps == other.HasManualScopedEntityProps &&
               HasManualScopedEntityInterface == other.HasManualScopedEntityInterface &&
               SequenceEqual(Properties, other.Properties) &&
               SequenceEqual(Navigations, other.Navigations) &&
               SequenceEqual(PrimitiveCollectionProperties, other.PrimitiveCollectionProperties) &&
               SequenceEqual(RelationAttributes, other.RelationAttributes) &&
               // Included on purpose: a finding can change without the attributes changing (the target
               // entity gained the navigation the Inverse points at), and the stale diagnostic must go.
               SequenceEqual(RelationDiagnostics, other.RelationDiagnostics) &&
               SequenceEqual(PartialTraits, other.PartialTraits) &&
               SequenceEqual(GeneratedRelationProperties, other.GeneratedRelationProperties) &&
               SequenceEqual(AllSourceMemberNames, other.AllSourceMemberNames) &&
               SequenceEqual(IgnoredPropertyNames, other.IgnoredPropertyNames) &&
               SequenceEqual(KeyColumns, other.KeyColumns);
    }

    public override int GetHashCode()
    {
        // Use a subset of fields for a fast hash — FullTypeName is the primary discriminator
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (FullTypeName?.GetHashCode() ?? 0);
            hash = hash * 31 + (IdType?.GetHashCode() ?? 0);
            hash = hash * 31 + IsFromReference.GetHashCode();
            hash = hash * 31 + Properties.Length;
            hash = hash * 31 + Navigations.Length;
            hash = hash * 31 + RelationAttributes.Length;
            hash = hash * 31 + GeneratedRelationProperties.Length;
            hash = hash * 31 + UniqueIndexes.Length;
            return hash;
        }
    }

    private static bool SequenceEqual<T>(EquatableArray<T> left, EquatableArray<T> right)
        where T : IEquatable<T>
    {
        if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty) return true;
        if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty) return false;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
                return false;
        }

        return true;
    }

    // string doesn't implement IEquatable<string> in the generic constraint sense for all frameworks,
    // so provide a dedicated overload for EquatableArray<string>
    private static bool SequenceEqual(EquatableArray<string> left, EquatableArray<string> right)
    {
        if (left.IsDefaultOrEmpty && right.IsDefaultOrEmpty) return true;
        if (left.IsDefaultOrEmpty || right.IsDefaultOrEmpty) return false;
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }
}

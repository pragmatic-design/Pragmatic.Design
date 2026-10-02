using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for generating a boundary-specific DbContext.
/// </summary>
internal sealed record BoundaryDbContextModel
{
    /// <summary>
    ///     The namespace for the generated DbContext.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     The name of the DbContext class (e.g., "SalesDbContext").
    /// </summary>
    public required string ClassName { get; init; }

    /// <summary>
    ///     The boundary name (typically the last segment of the namespace).
    /// </summary>
    public required string BoundaryName { get; init; }

    /// <summary>
    ///     The fully qualified boundary marker type (e.g., "Contoso.University.Students.StudentsBoundary").
    /// </summary>
    public string? BoundaryTypeName { get; init; }

    /// <summary>
    ///     All entities to include in this DbContext.
    /// </summary>
    public EquatableArray<DbContextEntityModel> Entities { get; init; } = EquatableArray<DbContextEntityModel>.Empty;

    /// <summary>
    ///     Entity types discovered via cross-boundary navigations that should be ignored in OnModelCreating.
    ///     These are entity types that EF Core would try to discover via navigation chains
    ///     but belong to a different boundary and have no configuration in this DbContext.
    /// </summary>
    public EquatableArray<string> CrossBoundaryEntityTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether this is a migration DbContext (contains all entities for a database).
    /// </summary>
    public bool IsMigrationContext { get; init; }

    /// <summary>
    ///     Custom DbContext class name from [Include&lt;TModule, TDatabase, TDbContext&gt;].
    ///     When set, overrides the auto-generated "{BoundaryName}DbContext" naming.
    /// </summary>
    public string? CustomDbContextName { get; init; }

    /// <summary>
    ///     Entities declared with [ReadAccess&lt;T&gt;] on this boundary.
    ///     These are included in the DbContext for SQL join queries but excluded from migrations.
    /// </summary>
    public EquatableArray<DbContextEntityModel> ReadAccessEntities { get; init; } =
        EquatableArray<DbContextEntityModel>.Empty;

    /// <summary>
    ///     Entities with [Inheritance] that need OnModelCreating configuration calls.
    ///     Each entry contains the fully qualified configuration class name
    ///     (e.g., "Showcase.Billing.Entities.FeeInheritanceConfiguration").
    /// </summary>
    public EquatableArray<string> InheritanceConfigurationTypes { get; init; } =
        EquatableArray<string>.Empty;

    /// <summary>
    ///     Whether to emit <c>modelBuilder.ApplyPragmaticInternationalization()</c> in OnModelCreating.
    ///     Set automatically by the generator when Pragmatic.Internationalization.EFCore is referenced.
    /// </summary>
    public bool HasI18nEFCore { get; init; }

    /// <summary>
    ///     The detected EF Core provider for this compilation.
    ///     Used to generate provider-specific concurrency configuration.
    /// </summary>
    public EfCoreProvider EfCoreProvider { get; init; }

    /// <summary>
    ///     Whether any entity in this DbContext is a tenant entity (<c>ITenantEntity</c>).
    ///     Drives the injected <c>ITenantContext</c> ctor parameter and the named <c>"Tenant"</c>
    ///     EF Core global query filter (defence-in-depth for raw <c>Set&lt;T&gt;()</c> queries).
    /// </summary>
    /// <remarks>
    ///     ⚠️ Owned entities only, and read-access ones deliberately excluded. This property answers
    ///     "is this boundary itself tenant-scoped", and two ad-hoc tables key their tenant filter off it
    ///     — <c>__SagaInstances</c> and <c>__BatchProgress</c>, neither of which is in
    ///     <see cref="Entities" />. Counting a read-access entity here switched those filters on for a
    ///     boundary that stores no tenant rows of its own, and since they are fail-closed the sagas
    ///     became invisible to every reader without a resolved tenant. Use
    ///     <see cref="RequiresTenantContext" /> for the different question of whether the context needs
    ///     the <c>ITenantContext</c> field at all.
    /// </remarks>
    public bool HasTenantEntities => Entities.Any(e => e.IsTenantEntity);

    /// <summary>
    ///     Whether this context has to be handed an <c>ITenantContext</c>: something it maps is
    ///     tenant-scoped, whether the boundary owns it or reads it through <c>[ReadAccess&lt;T&gt;]</c>.
    /// </summary>
    /// <remarks>
    ///     The named <c>"Tenant"</c> filter reads the field, and it is installed for read-access
    ///     entities too — so a boundary whose only tenant entity arrives that way still needs the field,
    ///     or the filter it emits does not compile.
    /// </remarks>
    public bool RequiresTenantContext
        => HasTenantEntities
           || ReadAccessEntities.Any(e => e.IsTenantEntity)
           || HasParentTenantEntities;

    /// <summary>
    ///     Whether anything mapped here is a trait child whose tenancy is its parent's.
    /// </summary>
    /// <remarks>
    ///     Such a child has no tenant column, so <see cref="HasTenantEntities" /> is blind to it — and
    ///     the filter that keeps it inside its tenant reads the same <c>ITenantContext</c> field.
    /// </remarks>
    public bool HasParentTenantEntities
        => Entities.Any(e => !string.IsNullOrEmpty(e.ParentTenantNavigation));

    /// <summary>
    ///     Whether any entity in this DbContext is <c>[Audited]</c>. Drives mapping the <c>__AuditLog</c>
    ///     table and wiring the <c>AuditLogInterceptor</c>.
    /// </summary>
    public bool HasAuditedEntities => Entities.Any(e => e.IsAudited);

    /// <summary>
    ///     Whether <c>Pragmatic.Privacy.EFCore</c> is referenced. Set by the generator from the detected
    ///     features.
    /// </summary>
    public bool HasPrivacyEFCore { get; init; }

    /// <summary>
    ///     Whether this context maps the subject registry's tables (<c>Subjects</c>, <c>Consents</c>):
    ///     the registry package is referenced and a <c>[DataSubject]</c> lives here.
    /// </summary>
    /// <remarks>
    ///     The same rule as the audit trail's tables, and for the same reason: the migration of the
    ///     database that holds the subjects creates the registry beside them. Nothing did before —
    ///     <c>PrivacyDbContext.ApplyPrivacyConfigurations</c> was documented for exactly this and had
    ///     no caller, so the registry failed on its first query.
    /// </remarks>
    public bool HasPrivacyRegistry => HasPrivacyEFCore && Entities.Any(e => e.IsDataSubject);

    /// <summary><c>Pragmatic.Cryptography.EFCore</c> is referenced.</summary>
    public bool HasCryptographyEFCore { get; init; }

    /// <summary>
    ///     Whether this context maps the per-subject key table: the package is referenced and a
    ///     <c>[DataSubject]</c> lives here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The same rule as the subject registry, and the keys belong beside it: one migration
    ///         creates the subject, its pseudonym and the key that opens what is protected about them.
    ///         ⚠️ Destroying that key is how a crypto-shredded column is erased, so a key in another
    ///         database would make an erasure a distributed transaction across two stores — with a
    ///         window in which the data is readable and the erasure says it is not.
    ///     </para>
    ///     <para>
    ///         Keyed on the subject and not on "an entity here holds a <c>ProtectedValue</c>", although
    ///         the second reads more precise: the entity model this context sees carries no property
    ///         list, and giving it one means setting a new field in six producers where forgetting one
    ///         is silent. Referencing <c>Cryptography.EFCore</c> is already a deliberate act, so the
    ///         table appears only where an application asked for it.
    ///     </para>
    /// </remarks>
    public bool HasSubjectKeys => HasCryptographyEFCore && Entities.Any(e => e.IsDataSubject);

    /// <summary>
    ///     Whether the boundary is marked <c>[EnableEventOutbox]</c>. Drives mapping the
    ///     <c>__EventOutbox</c> table, wiring the <c>EventOutboxInterceptor</c>, and registering the
    ///     delivery background service. Not derivable from entities — set from the boundary attribute.
    /// </summary>
    public bool HasEventOutbox { get; init; }

    /// <summary>
    ///     The boundary declares <c>[StoresNotifications]</c>, so <c>__Notifications</c> is mapped into
    ///     this context and created with the application's own tables.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not a <c>DbContext</c> of the package's own: a composed host creates one context per
    ///     declared database and nothing else, so the table would never be created and the first send
    ///     would fail at run time. A second context against the same database cannot fix
    ///     it: <c>EnsureCreated</c> is all-or-nothing per database.
    /// </remarks>
    public bool HasNotificationStore { get; init; }

    /// <summary>
    ///     Whether the boundary is marked <c>[EnableSagaPersistence]</c>. Drives mapping the
    ///     <c>__SagaInstances</c>/<c>__SagaSteps</c> tables so EF-backed saga repositories have a
    ///     schema. Not derivable from entities — set from the boundary attribute.
    /// </summary>
    public bool HasSagaPersistence { get; init; }

    /// <summary>
    ///     Whether the boundary is marked <c>[EnableOutbox]</c> (Messaging transport-publish outbox).
    ///     Drives mapping the <c>__OutboxMessages</c> table, wiring the <c>OutboxInterceptor</c>, and
    ///     registering the delivery pump and retention purge service. Not derivable from entities —
    ///     set from the boundary attribute.
    /// </summary>
    public bool HasMessagingOutbox { get; init; }

    /// <summary>
    ///     Whether the boundary is marked <c>[EnableBatchProgress]</c>. Drives mapping the
    ///     <c>__BatchProgress</c> table and registering <c>EfCoreBatchProgressStore</c> against this
    ///     DbContext. Batch progress is a single store, so at most one boundary carries this. Not
    ///     derivable from entities — set from the boundary attribute.
    /// </summary>
    public bool HasBatchProgress { get; init; }

    /// <summary>
    ///     Whether the boundary is marked <c>[EnableJobPersistence]</c>. Drives mapping the durable job
    ///     store's two tables, <c>__Jobs</c> and <c>__RecurringJobs</c>.
    /// </summary>
    /// <remarks>
    ///     The job store is a single store for the application, so at most one boundary carries this
    ///     (<c>PRAG2509</c>), and the flag needs <c>Pragmatic.Jobs.EFCore</c> on the compilation because
    ///     the generated context names its two configurations (<c>PRAG2508</c>). Not derivable from
    ///     entities — set from the boundary attribute. Which store to run stays the host's call
    ///     (<c>jobs.UseEfCore()</c>); what this settles is that the tables exist.
    /// </remarks>
    public bool HasJobPersistence { get; init; }

    /// <summary>
    ///     Whether the boundary is, or will be, an <c>IBoundary</c> — so an application can configure it, and
    ///     the registration applies what it passed to <c>UseDatabase</c>. Read from the boundary type by
    ///     <c>ConfigurableBoundaryReader</c>.
    /// </summary>
    public bool AppliesBoundaryConfiguration { get; init; }

    /// <summary>
    ///     Whether this model is valid for code generation.
    /// </summary>
    public bool IsValid => !string.IsNullOrEmpty(ClassName) && Entities.Length > 0;
}

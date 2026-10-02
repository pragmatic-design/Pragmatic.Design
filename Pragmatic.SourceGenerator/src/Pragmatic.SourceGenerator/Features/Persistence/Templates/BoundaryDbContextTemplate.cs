using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating a boundary-specific DbContext.
///     Generated in HOST project by reading entity metadata from referenced assemblies.
/// </summary>
internal sealed class BoundaryDbContextTemplate : CSharpTemplate
{
    private readonly BoundaryDbContextModel _model;

    public BoundaryDbContextTemplate(BoundaryDbContextModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput()
    {
        return new Artifact(VirtualFolderHints.ForDbContext(_model.BoundaryName), ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");

        if (_model.HasI18nEFCore)
            AddUsing("Pragmatic.Internationalization.EntityFrameworkCore.Extensions");

        // Add usings for entity namespaces (config classes are nested in entities, same namespace)
        foreach (var entity in _model.Entities)
        {
            var entityNs = GetNamespace(entity.FullTypeName);
            if (!string.IsNullOrEmpty(entityNs))
                AddUsing(entityNs);
        }

        // Add usings for inheritance configuration namespaces
        foreach (var configType in _model.InheritanceConfigurationTypes)
        {
            var configNs = GetNamespace(configType);
            if (!string.IsNullOrEmpty(configNs))
                AddUsing(configNs);
        }

        // Add usings for ReadAccess entity namespaces
        foreach (var entity in _model.ReadAccessEntities)
        {
            var entityNs = GetNamespace(entity.FullTypeName);
            if (!string.IsNullOrEmpty(entityNs))
                AddUsing(entityNs);
        }

        if (!string.IsNullOrEmpty(_model.Namespace))
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        RenderClass();
    }

    private static string GetNamespace(string fullTypeName)
    {
        // Handle global:: prefix
        var typeName = fullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring(8)
            : fullTypeName;

        var lastDot = typeName.LastIndexOf('.');
        return lastDot > 0 ? typeName.Substring(0, lastDot) : string.Empty;
    }

    private static string GetTypeName(string fullTypeName)
    {
        var typeName = fullTypeName.StartsWith("global::", StringComparison.Ordinal)
            ? fullTypeName.Substring(8)
            : fullTypeName;

        var lastDot = typeName.LastIndexOf('.');
        return lastDot > 0 ? typeName.Substring(lastDot + 1) : typeName;
    }

    private void RenderClass()
    {
        // Use fully qualified name to avoid namespace conflicts
        const string dbContextBaseType = "Microsoft.EntityFrameworkCore.DbContext";

        var description = _model.IsMigrationContext
            ? "Migration DbContext containing all entities for database schema management."
            : $"DbContext for the {_model.BoundaryName} boundary.";

        XmlSummary(description);
        AppendLine($"[global::Pragmatic.Persistence.EFCore.PragmaticDbContextAttribute(\"{_model.BoundaryName}\")]");

        Class(_model.ClassName, RenderBody,
            dbContextBaseType,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { Partial = true });
    }

    // FQN of the tenant context — kept fully qualified to avoid a using collision in the host.
    private const string TenantContextType = "global::Pragmatic.MultiTenancy.ITenantContext";

    private void RenderBody()
    {
        // Defence-in-depth tenant isolation: when the boundary has tenant entities, inject ITenantContext so
        // OnModelCreating can emit a named "Tenant" EF Core global query filter that also covers raw Set<T>()
        // queries (which bypass the runtime IQueryFilter pipeline). The parameter is nullable with a default so
        // the design-time/migration DbContext (no DI) stays constructible — no queries run during migrations,
        // so the filter expression is never evaluated. At runtime EF's AddDbContext resolves it from DI.
        //
        // RequiresTenantContext, not HasTenantEntities: a boundary whose only tenant entity arrives
        // through [ReadAccess<T>] gets the filter for it too, and the filter reads this field.
        if (_model.RequiresTenantContext)
        {
            AppendLine($"private readonly {TenantContextType}? _tenantContext;");
            AppendLine();

            XmlSummary($"Creates a new {_model.ClassName}.");
            XmlParam("options", "The DbContext options.");
            XmlParam("tenantContext", "The current tenant context (resolved from DI at runtime; null at design-time).");
            Constructor(_model.ClassName, () =>
                {
                    AppendLine("_tenantContext = tenantContext;");
                }, [
                    new MethodParameter($"DbContextOptions<{_model.ClassName}>", "options"),
                    new MethodParameter($"{TenantContextType}?", "tenantContext") { DefaultValue = "null" }
                ],
                AccessModifier.Public,
                "options");
        }
        else
        {
            // Constructor
            XmlSummary($"Creates a new {_model.ClassName}.");
            XmlParam("options", "The DbContext options.");
            Constructor(_model.ClassName, () =>
                {
                    // Empty body - base constructor handles everything
                }, [new MethodParameter($"DbContextOptions<{_model.ClassName}>", "options")],
                AccessModifier.Public,
                "options");
        }

        // DbSet properties for non-abstract entities
        // Use fully qualified type names to avoid namespace conflicts
        foreach (var entity in _model.Entities.Where(e => !e.IsAbstract))
        {
            AppendLine();
            XmlSummary($"Gets or sets the {entity.TypeName} entities.");
            AppendLine($"public DbSet<{entity.FullTypeName}> {entity.DbSetName} {{ get; set; }} = null!;");
        }

        // DbSet properties for ReadAccess entities (cross-boundary SQL join, excluded from migrations)
        foreach (var entity in _model.ReadAccessEntities.Where(e => !e.IsAbstract))
        {
            AppendLine();
            XmlSummary($"Gets or sets the {entity.TypeName} entities (ReadAccess — cross-boundary SQL join, excluded from migrations).");
            AppendLine($"public DbSet<{entity.FullTypeName}> {entity.DbSetName} {{ get; set; }} = null!;");
        }

        AppendLine();

        // The guarantee the name already implied.
        RenderReadAccessWriteGuard();

        // OnModelCreating
        RenderOnModelCreating();
    }

    /// <summary>
    ///     Refuses a write to an entity this boundary only has read access to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ Without this check <c>[ReadAccess&lt;T&gt;]</c> would be read-only <b>by name</b> only.
    ///         The <c>DbSet</c> it adds is writable like any other — <c>ExcludeFromMigrations</c> is about
    ///         the schema, not about permissions — and the write would reach the owner's row across the
    ///         boundary. Measured end to end in <c>ReadAccessAcrossTheBoundary</c>.
    ///     </para>
    ///     <para>
    ///         The check is on <c>SaveChanges</c> rather than on the <c>DbSet</c>, because it is the
    ///         save that crosses the boundary: a caller may attach one of those rows, read it, even
    ///         change it in memory — what it may not do is commit that change through <b>this</b>
    ///         unit of work, past every rule the owning boundary states about its own rows.
    ///     </para>
    ///     <para>
    ///         Both overloads, because either can be the one a caller reaches for, and a guarantee
    ///         that covers one of two is worse than none: the reader stops checking.
    ///     </para>
    /// </remarks>
    private void RenderReadAccessWriteGuard()
    {
        var writable = _model.ReadAccessEntities.Where(e => !e.IsAbstract).ToList();
        if (writable.Count == 0)
            return;

        XmlSummary("Refuses to commit a change to an entity this boundary only reads.");

        Method("EnsureReadAccessEntitiesAreUnchanged", () =>
        {
            AppendLine("foreach (var entry in ChangeTracker.Entries())");
            Block(() =>
            {
                AppendLine("if (entry.State is global::Microsoft.EntityFrameworkCore.EntityState.Unchanged");
                AppendLine("    or global::Microsoft.EntityFrameworkCore.EntityState.Detached)");
                Block(() => AppendLine("continue;"));
                AppendLine();
                AppendLine("var clrType = entry.Metadata.ClrType;");
                AppendLine("if (!IsReadOnlyHere(clrType))");
                Block(() => AppendLine("continue;"));
                AppendLine();
                AppendLine("throw new global::System.InvalidOperationException(");
                IncreaseIndent();
                AppendLine($"$\"{{clrType.Name}} is reachable from {_model.ClassName} through [ReadAccess], \"");
                AppendLine("+ $\"which grants reading and not writing: this {entry.State} would commit through \"");
                AppendLine($"+ \"the wrong unit of work, past the rules its own boundary states about it. \"");
                AppendLine("+ \"Raise a domain event and let the owning boundary write its own rows.\");");
                DecreaseIndent();
            });
        }, "void", null, AccessModifier.Private);

        XmlSummary("Whether the type is one this boundary only reads.");

        Method("IsReadOnlyHere", () =>
        {
            foreach (var entity in writable)
                AppendLine($"if (clrType == typeof({entity.FullTypeName})) return true;");

            AppendLine("return false;");
        }, "bool",
        [new MethodParameter("global::System.Type", "clrType")],
        AccessModifier.Private,
        new MethodModifiers { IsStatic = true });

        XmlSummary("Saves, refusing any change to an entity this boundary only reads.");

        Method("SaveChanges", () =>
        {
            AppendLine("EnsureReadAccessEntitiesAreUnchanged();");
            AppendLine("return base.SaveChanges(acceptAllChangesOnSuccess);");
        }, "int",
        [new MethodParameter("bool", "acceptAllChangesOnSuccess")],
        AccessModifier.Public,
        new MethodModifiers { IsOverride = true });

        XmlSummary("Saves, refusing any change to an entity this boundary only reads.");

        Method("SaveChangesAsync", () =>
        {
            AppendLine("EnsureReadAccessEntitiesAreUnchanged();");
            AppendLine("return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);");
        }, "global::System.Threading.Tasks.Task<int>",
        [
            new MethodParameter("bool", "acceptAllChangesOnSuccess"),
            new MethodParameter("global::System.Threading.CancellationToken", "cancellationToken")
                { DefaultValue = "default" }
        ],
        AccessModifier.Public,
        new MethodModifiers { IsOverride = true });
    }

    private void RenderOnModelCreating()
    {
        XmlSummary("Configures the model using entity configurations.");
        XmlParam("modelBuilder", "The model builder.");
        Method("OnModelCreating", () =>
            {
                AppendLine("base.OnModelCreating(modelBuilder);");
                AppendLine();

                // Apply explicit configurations (nested EntityConfig classes)
                Comment("Apply entity configurations");
                foreach (var entity in _model.Entities)
                {
                    AppendLine($"modelBuilder.ApplyConfiguration(new {entity.ConfigurationTypeName}());");
                }

                // [Audited] entities append to the framework audit trail, written by AuditLogInterceptor
                // into this very context — which is what makes the entry and the change one transaction.
                if (_model.HasAuditedEntities)
                {
                    AppendLine();
                    Comment("Audit trail (__AuditEntries/__AuditSegments) for [Audited] entities");
                    AppendLine("global::Pragmatic.Audit.EFCore.AuditDbContext.ApplyAuditConfigurations(modelBuilder);");
                }

                // A [DataSubject] lives here: the subject registry (Subjects, Consents) is created by the
                // same migration, in the database that holds the people it maps.
                if (_model.HasPrivacyRegistry)
                {
                    AppendLine();
                    Comment("Subject registry (Subjects/Consents) for the [DataSubject] entities of this boundary");
                    AppendLine("global::Pragmatic.Privacy.EFCore.PrivacyDbContext.ApplyPrivacyConfigurations(modelBuilder);");
                }

                // A ProtectedValue lives here: the key that opens it is created by the same migration,
                // in the same database. Erasing such a column is destroying its key, and a key in
                // another store would make that a distributed transaction.
                if (_model.HasSubjectKeys)
                {
                    AppendLine();
                    Comment("Per-subject keys (__SubjectKeys) for the ProtectedValue columns of this boundary");
                    AppendLine("global::Pragmatic.Cryptography.EFCore.CryptographyDbContext.ApplyCryptographyConfigurations(modelBuilder);");
                }

                // [StoresNotifications] boundary: map __Notifications here, so the table the EF store
                // writes is created and migrated with the application's own. The package's
                // own DbContext is not created by a composed host, and a second context against the
                // same database creates none of its tables — EnsureCreated is all-or-nothing.
                if (_model.HasNotificationStore)
                {
                    AppendLine();
                    Comment("Notification records (__Notifications) for the boundary that declared [StoresNotifications]");
                    AppendLine("global::Pragmatic.Notifications.EFCore.NotificationDbContext.ApplyNotificationConfigurations(modelBuilder);");
                }

                // [EnableEventOutbox] boundary: map the __EventOutbox table (written by EventOutboxInterceptor)
                if (_model.HasEventOutbox)
                {
                    AppendLine();
                    Comment("Transactional event outbox (__EventOutbox) — [EnableEventOutbox] boundary");
                    AppendLine("modelBuilder.ApplyConfiguration(new global::Pragmatic.Events.EFCore.Outbox.EventOutboxEntryConfiguration());");
                }

                // [EnableSagaPersistence] boundary: map __SagaInstances/__SagaSteps (one config implements both).
                if (_model.HasSagaPersistence)
                {
                    AppendLine();
                    Comment("Saga persistence (__SagaInstances/__SagaSteps) — [EnableSagaPersistence] boundary");
                    AppendLine("var __sagaConfig = new global::Pragmatic.Messaging.Saga.SagaEntityTypeConfiguration();");
                    AppendLine("modelBuilder.ApplyConfiguration<global::Pragmatic.Messaging.Saga.SagaInstance>(__sagaConfig);");
                    AppendLine("modelBuilder.ApplyConfiguration<global::Pragmatic.Messaging.Saga.SagaStep>(__sagaConfig);");
                }

                // [EnableOutbox] boundary: map the __OutboxMessages table (written by OutboxInterceptor)
                if (_model.HasMessagingOutbox)
                {
                    AppendLine();
                    Comment("Transactional outbox (__OutboxMessages) — [EnableOutbox] boundary");
                    AppendLine("global::Pragmatic.Messaging.EFCore.Outbox.MessagingOutboxExtensions.AddMessagingOutbox(modelBuilder);");
                }

                // [EnableBatchProgress] boundary: map the __BatchProgress table (single-owner batch store).
                if (_model.HasBatchProgress)
                {
                    AppendLine();
                    Comment("Batch progress (__BatchProgress) — [EnableBatchProgress] boundary");
                    AppendLine("modelBuilder.ApplyConfiguration(new global::Pragmatic.Messaging.Batch.BatchProgressEntityTypeConfiguration());");
                }

                // [EnableJobPersistence] boundary: map __Jobs/__RecurringJobs (single-owner job store).
                // A framework table is mapped here, not left to OnModelCreatingPartial: jobs.UseEfCore()
                // refuses to run against tables nobody created, and the declaration is what creates them.
                if (_model.HasJobPersistence)
                {
                    AppendLine();
                    Comment("Durable jobs (__Jobs/__RecurringJobs) — [EnableJobPersistence] boundary");
                    AppendLine("modelBuilder.ApplyConfiguration(new global::Pragmatic.Jobs.EFCore.Entities.JobEntityTypeConfiguration());");
                    AppendLine("modelBuilder.ApplyConfiguration(new global::Pragmatic.Jobs.EFCore.Entities.RecurringJobEntityTypeConfiguration());");
                }

                // Apply inheritance mapping configurations (TPH/TPT/TPC)
                if (!_model.InheritanceConfigurationTypes.IsDefaultOrEmpty)
                {
                    AppendLine();
                    Comment("Configure inheritance mappings (TPH, TPT, TPC)");
                    foreach (var configType in _model.InheritanceConfigurationTypes)
                    {
                        var configTypeName = GetTypeName(configType);
                        AppendLine($"{configTypeName}.Configure(modelBuilder);");
                    }
                }

                // ReadAccess entities: apply configuration + exclude from migrations
                if (!_model.ReadAccessEntities.IsDefaultOrEmpty)
                {
                    AppendLine();
                    Comment("ReadAccess entities: included for SQL join, excluded from migrations");
                    foreach (var entity in _model.ReadAccessEntities)
                    {
                        AppendLine($"modelBuilder.ApplyConfiguration(new {entity.ConfigurationTypeName}());");
                        AppendLine($"modelBuilder.Entity<{entity.FullTypeName}>().ToTable(t => t.ExcludeFromMigrations());");
                    }

                    RenderSelfReferentialSkipNavigationIgnores();
                }

                // Ignore cross-boundary entities discovered via navigation chains.
                // IMPORTANT: must come AFTER all ApplyConfiguration calls — EF Core re-adds types
                // that are referenced by explicitly configured relationships (e.g. ManyToMany.UsingEntity).
                if (!_model.CrossBoundaryEntityTypes.IsDefaultOrEmpty)
                {
                    AppendLine();
                    Comment("Ignore entities from other boundaries (discovered via navigation chains)");
                    foreach (var entityType in _model.CrossBoundaryEntityTypes)
                        AppendLine($"modelBuilder.Ignore<{entityType}>();");
                }

                // Tenant isolation: defence-in-depth named "Tenant" EF Core global query filter.
                RenderTenantQueryFilters();

                // Saga persistence rows are tenant-isolated too (ad-hoc table, not in _model.Entities).
                RenderSagaTenantQueryFilter();

                // Batch progress rows are tenant-isolated too (ad-hoc table, not in _model.Entities).
                RenderBatchTenantQueryFilter();

                // Concurrency tokens — provider-specific strategy applied at Host level
                RenderConcurrencyConfiguration();

                // Logic-key partial unique indexes — raw SQL filter is provider-specific
                RenderLogicKeyFilteredIndexes();
                RenderTemporalMaxActiveIndexes();

                // Apply i18n conventions: CurrencyCode → varchar(3), LocalizedString → JSON column.
                // Emitted only when Pragmatic.Internationalization.EFCore is referenced.
                if (_model.HasI18nEFCore)
                {
                    AppendLine();
                    Comment("Apply Pragmatic.Internationalization EF Core conventions (CurrencyCode, LocalizedString)");
                    AppendLine("modelBuilder.ApplyPragmaticInternationalization();");
                }

                // Last, so the application's part sees the whole generated model and can override it.
                AppendLine();
                Comment("The application's part of this context: runs after every generated configuration");
                AppendLine("OnModelCreatingPartial(modelBuilder);");
            }, "void",
            [new MethodParameter("ModelBuilder", "modelBuilder")],
            AccessModifier.Protected,
            new MethodModifiers { IsOverride = true });

        // OnModelCreating is an override, so a partial class of this context cannot add to it; this is
        // the seam that can. The EF-scaffolding convention: free when nobody implements it.
        AppendLine();
        XmlSummary("Adds to the model after every generated configuration. Implement it in a partial class of this context.");
        XmlParam("modelBuilder", "The model builder.");
        AppendLine("partial void OnModelCreatingPartial(ModelBuilder modelBuilder);");
    }

    /// <summary>
    ///     Emits a named <c>"Tenant"</c> EF Core global query filter for every tenant entity. This is the
    ///     defence-in-depth complement to the runtime <c>TenantFilter</c> (IQueryFilter pipeline): the EF filter
    ///     also applies to raw <c>Set&lt;T&gt;()</c> / <c>DbSet</c> access that bypasses the Pragmatic pipeline.
    ///     Fail-closed, mirroring <c>TenantFilterTemplate</c>: an unresolved tenant (<c>TenantId == null</c>)
    ///     matches NO rows. Named so it AND-combines with the entity config's <c>"SoftDelete"</c> filter
    ///     instead of replacing it (EF Core 10 named-filter semantics).
    /// </summary>
    /// <remarks>
    ///     Read-access entities are in the list. An entity that arrives through <c>[ReadAccess&lt;T&gt;]</c>
    ///     brings its owner's per-entity config with it — so <c>"SoftDelete"</c> and the visibility rules
    ///     cross the line — but <c>"Tenant"</c> is not in that config: it needs the scoped
    ///     <c>ITenantContext</c>, which only a DbContext has. Iterating <c>Entities</c> alone left the
    ///     read-access <c>DbSet</c> with <c>!IsDeleted</c> and nothing else, and a reader of another
    ///     boundary's rows was a reader of every tenant's. Readable is not owned; it is not global either.
    /// </remarks>
    /// <summary>
    ///     Drops the self-referential skip navigations a <c>[ReadAccess]</c> entity brought with it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         After <c>ApplyConfiguration</c>, which is what declared them, and before the
    ///         <c>Ignore&lt;T&gt;()</c> of the cross-boundary types, which is what leaves them dangling:
    ///         once the navigation is gone the join entity has no referrer and the ignore holds.
    ///     </para>
    ///     <para>
    ///         By name rather than by lambda: the navigation is on a type from another assembly and the
    ///         reading context has no reason to know its shape, and <c>Ignore(string)</c> is the overload
    ///         that does not need it.
    ///     </para>
    /// </remarks>
    private void RenderSelfReferentialSkipNavigationIgnores()
    {
        var withSelfReferences = _model.ReadAccessEntities
            .Where(e => !e.SelfReferentialSkipNavigations.IsDefaultOrEmpty)
            .ToList();

        if (withSelfReferences.Count == 0)
            return;

        AppendLine();
        Comment("A self-referential many-to-many on a read entity: the target is the entity itself, so");
        Comment("ignoring the type cannot remove it, and its join entity is not here. EF rejects the");
        Comment("model at first use — \"the skip navigation doesn't have a foreign key\" — so it goes.");

        foreach (var entity in withSelfReferences)
        foreach (var navigation in entity.SelfReferentialSkipNavigations)
            AppendLine($"modelBuilder.Entity<{entity.FullTypeName}>().Ignore(\"{navigation}\");");
    }

    private void RenderTenantQueryFilters()
    {
        var tenantEntities = _model.Entities
            .Concat(_model.ReadAccessEntities)
            .Where(e => e.IsTenantEntity)
            .ToList();

        var parentTenantEntities = _model.Entities
            .Where(e => !string.IsNullOrEmpty(e.ParentTenantNavigation))
            .ToList();

        if (tenantEntities.Count == 0 && parentTenantEntities.Count == 0)
            return;

        AppendLine();
        Comment("Defence-in-depth tenant isolation: named global query filter also covers raw Set<T>() queries.");
        Comment("Fail-closed — an unresolved tenant (TenantId == null) matches no rows.");
        foreach (var entity in tenantEntities)
            AppendLine(
                $"modelBuilder.Entity<{entity.FullTypeName}>().HasQueryFilter(\"Tenant\", " +
                $"e => _tenantContext != null && _tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);");

        if (parentTenantEntities.Count == 0)
            return;

        AppendLine();
        Comment("A trait child has no tenant column — its tenancy is its parent's — and its generated list");
        Comment("query filters by the parent id alone, so a caller holding an id from another tenant read the");
        Comment("whole collection. The parent's own filter is lifted here through the reference navigation.");
        Comment("Not the ParentVisibilityFilter: that one honours the parent's view-all bypass, and no");
        Comment("permission may cross a tenant. A child whose parent row is gone is not visible either.");
        foreach (var entity in parentTenantEntities)
        {
            var nav = $"e.{entity.ParentTenantNavigation}";
            AppendLine(
                $"modelBuilder.Entity<{entity.FullTypeName}>().HasQueryFilter(\"ParentTenant\", " +
                $"e => _tenantContext != null && _tenantContext.TenantId != null && {nav} != null " +
                $"&& {nav}.TenantId == _tenantContext.TenantId);");
        }
    }

    /// <summary>
    ///     Emits the fail-closed <c>"Tenant"</c> EF Core global query filter for the ad-hoc saga instance
    ///     table (mirrors <see cref="RenderTenantQueryFilters"/>, which only covers <c>_model.Entities</c>).
    ///     Gated on the boundary having BOTH saga persistence AND tenant entities: the latter guarantees the
    ///     <c>_tenantContext</c> field exists and the host is multi-tenant, so a single-tenant host that only
    ///     enables saga persistence keeps its sagas unfiltered (unchanged behaviour). Background/admin reads in
    ///     <c>EfCoreSagaRepository</c> (timeout scan, GetActive) bypass this via <c>IgnoreQueryFilters()</c>.
    /// </summary>
    private void RenderSagaTenantQueryFilter()
    {
        if (!_model.HasSagaPersistence || !_model.HasTenantEntities)
            return;

        AppendLine();
        Comment("Tenant isolation for saga instances (fail-closed — unresolved tenant matches no rows).");
        AppendLine(
            "modelBuilder.Entity<global::Pragmatic.Messaging.Saga.SagaInstance>().HasQueryFilter(\"Tenant\", " +
            "e => _tenantContext != null && _tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);");
    }

    /// <summary>
    ///     Emits the fail-closed <c>"Tenant"</c> EF Core global query filter for the ad-hoc batch-progress
    ///     table (mirrors <see cref="RenderSagaTenantQueryFilter"/>). Gated on the boundary having BOTH
    ///     batch progress AND tenant entities, so a single-tenant host keeps its batches unfiltered.
    ///     Background/admin reads in <c>EfCoreBatchProgressStore.GetActiveAsync</c> bypass this via
    ///     <c>IgnoreQueryFilters()</c>.
    /// </summary>
    private void RenderBatchTenantQueryFilter()
    {
        if (!_model.HasBatchProgress || !_model.HasTenantEntities)
            return;

        AppendLine();
        Comment("Tenant isolation for batch progress (fail-closed — unresolved tenant matches no rows).");
        AppendLine(
            "modelBuilder.Entity<global::Pragmatic.Messaging.Batch.BatchProgress>().HasQueryFilter(\"Tenant\", " +
            "e => _tenantContext != null && _tenantContext.TenantId != null && e.TenantId == _tenantContext.TenantId);");
    }

    /// <summary>
    ///     Filters the logic-key unique index of soft-delete entities so deleted rows do not
    ///     block re-insert. <c>HasFilter</c> is raw SQL — quoting and boolean literals differ
    ///     per provider, so the filter lives here (the EntityConfig emits the bare unique
    ///     index, which EF Core merges with this configuration by property set).
    /// </summary>
    private void RenderLogicKeyFilteredIndexes()
    {
        var filteredEntities = _model.Entities
            .Where(e => e is { IsSoftDelete: true } && !string.IsNullOrEmpty(e.LogicKeyPropertyName))
            .ToList();
        if (filteredEntities.Count == 0)
            return;

        // PostgreSQL: quoted identifier + true/false literals. SQL Server: bracketed identifier,
        // bit literal. Default (SQLite & co.): quoted identifier + 0/1 (SQLite has no booleans).
        var filterSql = _model.EfCoreProvider switch
        {
            EfCoreProvider.PostgreSql => "\\\"IsDeleted\\\" = false",
            EfCoreProvider.SqlServer => "[IsDeleted] = 0",
            _ => "\\\"IsDeleted\\\" = 0",
        };

        AppendLine();
        Comment("Soft-delete partial unique index on the logic key (provider-specific filter SQL)");
        foreach (var entity in filteredEntities)
            AppendLine(
                $"modelBuilder.Entity<{entity.FullTypeName}>()" +
                $".HasIndex({entity.LogicKeySelector ?? $"e => e.{entity.LogicKeyPropertyName}"})" +
                $".IsUnique().HasFilter(\"{filterSql}\");");
    }

    /// <summary>
    ///     Makes <c>MaxActive = 1</c> a property of the data: one open stretch per parent, enforced by
    ///     the database on every write path.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ The generated mutation invoker's check is not enough on its own. An action writing the
    ///         same entity through the same repository never reaches it: two overlapping open stretches
    ///         written with a <c>200</c> where a mutation answers <c>409</c>.
    ///         <c>[TemporalRelation(MaxActive = 1)]</c> reads as an invariant of the
    ///         entity, so it has to hold wherever the entity is written, and a partial unique index is
    ///         the only place that is true of.
    ///     </para>
    ///     <para>
    ///         The filter is raw provider-specific SQL, which is why this lives here rather than in the
    ///         EntityConfig — the same reason, and the same shape, as the soft-delete logic-key index
    ///         above. EF Core merges the two configurations by property set.
    ///     </para>
    ///     <para>
    ///         ⚠️ <b>This is a schema change.</b> An existing deployment gets a migration, and it will
    ///         fail to apply if the data already violates the constraint — which is the point: those
    ///         rows are the damage the missing enforcement allowed.
    ///     </para>
    /// </remarks>
    private void RenderTemporalMaxActiveIndexes()
    {
        var temporalEntities = _model.Entities
            .Where(e => !string.IsNullOrEmpty(e.TemporalMaxOneParentKey))
            .ToList();
        if (temporalEntities.Count == 0)
            return;

        // Only the identifier quoting differs; IS NULL is the same everywhere.
        var quote = _model.EfCoreProvider switch
        {
            EfCoreProvider.SqlServer => ("[", "]"),
            _ => ("\\\"", "\\\""),
        };

        AppendLine();
        Comment("MaxActive = 1: one open stretch per parent, enforced by the database on every write");
        foreach (var entity in temporalEntities)
            AppendLine(
                $"modelBuilder.Entity<{entity.FullTypeName}>()" +
                $".HasIndex(e => e.{entity.TemporalMaxOneParentKey})" +
                $".IsUnique().HasFilter(\"{quote.Item1}ValidTo{quote.Item2} IS NULL\");");
    }

    private void RenderConcurrencyConfiguration()
    {
        var concurrencyEntities = _model.Entities.Where(e => e.IsConcurrencyAware).ToList();
        if (concurrencyEntities.Count == 0)
            return;

        AppendLine();

        switch (_model.EfCoreProvider)
        {
            case EfCoreProvider.SqlServer:
                Comment("Optimistic concurrency: SQL Server native rowversion (auto-managed by database)");
                foreach (var entity in concurrencyEntities)
                    AppendLine($"modelBuilder.Entity<{entity.FullTypeName}>().Property<byte[]>(\"RowVersion\").IsRowVersion();");
                break;

            case EfCoreProvider.PostgreSql:
                // Npgsql 10+ auto-detects uint + IsConcurrencyToken + ValueGeneratedOnAddOrUpdate
                // and maps it to the PostgreSQL xmin system column (no migration, no entity property needed).
                Comment("Optimistic concurrency: PostgreSQL xmin via uint shadow property (Npgsql auto-maps to xmin)");
                foreach (var entity in concurrencyEntities)
                    AppendLine(
                        $"modelBuilder.Entity<{entity.FullTypeName}>().Property<uint>(\"RowVersion\").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();");
                break;

            default:
                Comment("Optimistic concurrency: uint shadow property with IsConcurrencyToken (portable)");
                foreach (var entity in concurrencyEntities)
                    AppendLine($"modelBuilder.Entity<{entity.FullTypeName}>().Property<uint>(\"RowVersion\").IsConcurrencyToken();");
                break;
        }
    }
}

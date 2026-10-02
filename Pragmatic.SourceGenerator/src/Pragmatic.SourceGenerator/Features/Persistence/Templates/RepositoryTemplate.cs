using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating {EntityName}Repository class.
///     Repository provides CRUD operations, GetByLogicKey, and extension points.
/// </summary>
internal sealed class RepositoryTemplate : CSharpTemplate
{
    private readonly EntityMetadataModel _model;
    private readonly string _repositoryClassName;
    private readonly EfCoreProvider _provider;

    public RepositoryTemplate(EntityMetadataModel model, EfCoreProvider provider = EfCoreProvider.Generic)
    {
        _model = model;
        _provider = provider;
        _repositoryClassName = "Repository";
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";
    protected override string? SourceInfo => $"{_model.TypeName} from {_model.Namespace}";
    protected override string? TriggerInfo => $"[Entity] on {_model.TypeName}";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForType(_model.TypeName, "Repository", _model.Namespace),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _model.IsValid;
    }

    public override void RenderFile()
    {
        AddUsing("System");
        AddUsing("System.Linq");
        AddUsing("System.Threading");
        AddUsing("System.Threading.Tasks");
        AddUsing("System.Collections.Generic");
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.EntityFrameworkCore.Query");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Pragmatic.Persistence.EFCore.Bulk");
        AddUsing("Pragmatic.Specification");
        AddUsing("Pragmatic.Persistence.Query.Filters");

        if (_model.IsSoftDelete || _model.IsAuditable || _model.IsAudited)
        {
            AddUsing("Pragmatic.Identity");
        }

        if (!string.IsNullOrEmpty(_model.Namespace) && _model.Namespace != "<global namespace>")
        {
            AppendNamespace(_model.Namespace);
            AppendLine();
        }

        // Nested inside partial entity class
        Class(_model.TypeName, RenderClass,
            accessModifier: ParseAccessibility(_model.Accessibility),
            modifiers: new ClassModifiers { Partial = true });
    }

    private void RenderClass()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var idType = GetFullIdType();

        var interfaces = new List<string>
        {
            $"global::Pragmatic.Persistence.Repository.IRepository<{entityType}>",
            // The top-up an invoker needs when a caller hands it an entity: declared where there is no
            // EF Core, implemented here, where the DbContext is.
            $"global::Pragmatic.Persistence.Repository.INavigationLoader<{entityType}>",
            // And the same for choosing which rows a navigation points at, from their keys alone:
            // attaching one means writing to the change tracker, which needs the context too.
            $"global::Pragmatic.Persistence.Repository.INavigationLinker<{entityType}>"
        };

        XmlSummary(
            $"Repository for {_model.TypeName} entity. Provides CRUD operations and query methods.");

        Class(_repositoryClassName, RenderClassBody,
            interfaces: interfaces,
            accessModifier: ParseAccessibility(_model.Accessibility),
            // `new` on a derived entity: its base is an entity too and carries a nested type of the same
            // name over a different entity, so hiding is exactly what is meant. Without the keyword it
            // is CS0108 — a warning the repository compiles as an error, which is what stopped a derived
            // [Entity] from building at all.
            modifiers: new ClassModifiers
            {
                Partial = true,
                New = _model.BaseEntityFullTypeName is not null
            });
    }

    private void RenderClassBody()
    {
        var entityType = $"global::{_model.FullTypeName}";
        var idType = GetFullIdType();

        // DbContext field
        AppendLine("private readonly global::Microsoft.EntityFrameworkCore.DbContext _db;");

        // Every save goes through the unit of work, never through _db directly. It is the one place
        // that knows a save happened: it classifies what the database refused into a domain error,
        // hands the entities' domain events to whoever owns the commit, and records the activity and
        // the row count. Saving through the DbContext skipped all three, so a write made here behaved
        // differently from the same write made by a mutation.
        AppendLine("private readonly global::Pragmatic.Persistence.Repository.IUnitOfWork _unitOfWork;");

        // Query filter provider for global filters (soft-delete, tenant, authorization)
        AppendLine("private readonly global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider? _filterProvider;");

        // FilterMapComposer for navigation-level filtering (visitor-based)
        AppendLine("private readonly global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer? _filterMapComposer;");

        // Tenant context and filter toggle for building FilterContext
        AppendLine("private readonly global::Pragmatic.MultiTenancy.ITenantContext? _tenantContext;");
        AppendLine("private readonly global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle? _filterToggle;");

        // The executor that runs a declared query. Optional so a repository built by hand — a test
        // double, a container that predates this — still constructs; RunAsync says what is missing.
        AppendLine("private readonly global::Pragmatic.Persistence.Query.Executors.IQueryExecutor? _executor;");

        // True when a roll-up rule targets this entity as its child: bulk delete must then use the
        // tracked path so the RollUpInterceptor recomputes parent aggregates.
        AppendLine("private readonly bool _participatesInRollUp;");

        // ⚠️ The clock is unconditional, and ICurrentUser is not. Every repository builds a
        // FilterContext, and FilterContext.Now is `required` precisely so that the instant a read
        // evaluates against is something a caller states rather than something that happens, and a
        // default of UtcNow could not be pinned. A repository without _timeProvider would fill that
        // required property from the wall clock anyway, which is the same defect one layer down: the
        // entity's traits would decide whether the application could pin its own reads.
        AppendLine("private readonly global::System.TimeProvider _timeProvider;");

        if (_model.IsSoftDelete || _model.IsAuditable || _model.IsAudited)
            AppendLine("private readonly global::Pragmatic.Identity.ICurrentUser? _currentUser;");

        AppendLine();

        // Constructor with keyed DI
        RenderConstructor();
        AppendLine();

        // DbSet property
        XmlSummary($"Gets the DbSet for {_model.TypeName}.");
        ExpressionProperty("Set", $"global::Microsoft.EntityFrameworkCore.DbSet<{entityType}>", $"_db.Set<{entityType}>()");
        AppendLine();

        // DbContext property (internal for extension methods)
        XmlSummary("Gets the underlying DbContext.");
        ExpressionProperty("Context", "global::Microsoft.EntityFrameworkCore.DbContext", "_db", AccessModifier.Internal);
        AppendLine();

        // Query filter helper
        RenderApplyFilters(entityType);
        AppendLine();

        // The fourth rung: a declared query, run through the repository the operation already holds.
        RenderRunAsync(entityType);

        // Core methods
        RenderGetByIdAsync(entityType, idType);
        AppendLine();

        RenderGetByIdWithIncludesAsync(entityType, idType);
        AppendLine();

        RenderAdd(entityType);
        AppendLine();

        RenderAddRange(entityType);
        AppendLine();

        RenderRemove(entityType);
        AppendLine();

        RenderRemoveRange(entityType);
        AppendLine();

        RenderUpdate(entityType);
        AppendLine();

        // IReadRepository methods
        RenderFindAsync(entityType);
        AppendLine();

        RenderCountAsync(entityType);
        AppendLine();

        RenderExistsAsync(entityType);
        AppendLine();

        RenderFirstOrDefaultAsync(entityType);
        AppendLine();

        RenderQuery(entityType);

        // GetByLogicKey if entity has one
        if (!string.IsNullOrEmpty(_model.LogicKey))
        {
            AppendLine();
            RenderGetByLogicKey(entityType);
        }

        // Bulk operations
        AppendLine();
        RenderBulkDescriptor(entityType);
        AppendLine();
        RenderBulkUpdateAsync(entityType);
        AppendLine();
        RenderBulkDeleteAsync(entityType);
        AppendLine();
        RenderBulkInsertAsync(entityType);
        AppendLine();
        RenderBulkUpsertAsync(entityType);
        AppendLine();
        RenderUpsertAsync(entityType);

        AppendLine();
        RenderSaveChangesAsync();
    }

    private void RenderConstructor()
    {
        // Use boundary type as keyed DI key (matches DbContextRegistrationTemplate)
        var keyed = KeyedServiceAttribute();

        XmlSummary($"Creates a new {_repositoryClassName}.");
        XmlParam("db", "The database context (injected via keyed DI with boundary type as key).");
        XmlParam("unitOfWork",
            "The boundary's unit of work (same keyed DI key as the context, so it is the same instance an "
            + "invoker holds). Every save goes through it: it classifies database rule violations, hands "
            + "over domain events, and records the save.");
        XmlParam("filterProvider", "Optional query filter provider for root-level filters (soft-delete, tenant, authorization).");
        XmlParam("filterMapComposer", "Optional composer for navigation-level filtering via expression visitor.");
        XmlParam("tenantContext", "Optional tenant context for building FilterContext.");
        XmlParam("filterToggle", "Optional filter toggle for FilterMode in FilterContext.");
        XmlParam("rollUpRules", "Registered roll-up rules; used to detect whether this entity is a roll-up child so bulk delete routes through the tracked path that keeps parent aggregates current.");
        XmlParam("executor", "Runs a declared query against this repository's set; only RunAsync needs it.");

        // Documented once, because the parameter is now on both constructors: every repository builds
        // a FilterContext and FilterContext.Now is required. Leaving it in the branch below cost a
        // CS1573 on every entity that carries no audit trait — the tag and the parameter have to move
        // together.
        XmlParam("timeProvider",
            "Optional TimeProvider. Supplies FilterContext.Now, and the audit timestamps where the "
            + "entity has them. Falls back to TimeProvider.System if null.");

        if (_model.IsSoftDelete || _model.IsAuditable || _model.IsAudited)
        {
            XmlParam("currentUser", "Optional current user for populating audit fields. When null, user fields are left as null.");

            AppendLine($"public {_repositoryClassName}(");
            IncreaseIndent();
            AppendLine($"{keyed}global::Microsoft.EntityFrameworkCore.DbContext db,");
            AppendLine($"{keyed}global::Pragmatic.Persistence.Repository.IUnitOfWork unitOfWork,");
            AppendLine("global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider? filterProvider = null,");
            AppendLine("global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer? filterMapComposer = null,");
            AppendLine("global::Pragmatic.MultiTenancy.ITenantContext? tenantContext = null,");
            AppendLine("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle? filterToggle = null,");
            AppendLine("global::System.TimeProvider? timeProvider = null,");
            AppendLine("global::Pragmatic.Identity.ICurrentUser? currentUser = null,");
            AppendLine("global::System.Collections.Generic.IEnumerable<global::Pragmatic.Persistence.RollUp.RollUpRule>? rollUpRules = null,");
            AppendLine("global::Pragmatic.Persistence.Query.Executors.IQueryExecutor? executor = null)");
            DecreaseIndent();
            Block(() =>
            {
                AppendLine("_db = db ?? throw new global::System.ArgumentNullException(nameof(db));");
                AppendLine("_unitOfWork = unitOfWork ?? throw new global::System.ArgumentNullException(nameof(unitOfWork));");
                AppendLine("_filterProvider = filterProvider;");
                AppendLine("_filterMapComposer = filterMapComposer;");
                AppendLine("_tenantContext = tenantContext;");
                AppendLine("_filterToggle = filterToggle;");
                AppendLine("_timeProvider = timeProvider ?? global::System.TimeProvider.System;");
                AppendLine("_currentUser = currentUser;");
                AppendLine("_executor = executor;");
                RenderRollUpParticipationAssignment();
            });
        }
        else
        {
            AppendLine($"public {_repositoryClassName}(");
            IncreaseIndent();
            AppendLine($"{keyed}global::Microsoft.EntityFrameworkCore.DbContext db,");
            AppendLine($"{keyed}global::Pragmatic.Persistence.Repository.IUnitOfWork unitOfWork,");
            AppendLine("global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider? filterProvider = null,");
            AppendLine("global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer? filterMapComposer = null,");
            AppendLine("global::Pragmatic.MultiTenancy.ITenantContext? tenantContext = null,");
            AppendLine("global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle? filterToggle = null,");
            AppendLine("global::System.TimeProvider? timeProvider = null,");
            AppendLine("global::System.Collections.Generic.IEnumerable<global::Pragmatic.Persistence.RollUp.RollUpRule>? rollUpRules = null,");
            AppendLine("global::Pragmatic.Persistence.Query.Executors.IQueryExecutor? executor = null)");
            DecreaseIndent();
            Block(() =>
            {
                AppendLine("_db = db ?? throw new global::System.ArgumentNullException(nameof(db));");
                AppendLine("_unitOfWork = unitOfWork ?? throw new global::System.ArgumentNullException(nameof(unitOfWork));");
                AppendLine("_filterProvider = filterProvider;");
                AppendLine("_filterMapComposer = filterMapComposer;");
                AppendLine("_tenantContext = tenantContext;");
                AppendLine("_filterToggle = filterToggle;");
                AppendLine("_timeProvider = timeProvider ?? global::System.TimeProvider.System;");
                AppendLine("_executor = executor;");
                RenderRollUpParticipationAssignment();
            });
        }
    }

    // _participatesInRollUp = any registered roll-up rule has this entity as its child type.
    private void RenderRollUpParticipationAssignment()
        => AppendLine(
            "_participatesInRollUp = rollUpRules != null && global::System.Linq.Enumerable.Any(rollUpRules, "
            + $"static r => r.ChildType == typeof(global::{_model.FullTypeName}));");

    /// <summary>
    ///     Gets the type used as keyed DI service key.
    ///     Uses boundary type when available (matches DbContextRegistrationTemplate),
    ///     falls back to repository class name.
    /// </summary>
    /// <summary>
    ///     The attribute that keys the context and the unit of work, or nothing at all.
    /// </summary>
    /// <remarks>
    ///     ⚠️ No fallback to the repository class itself as the key — a key nobody registers, so the
    ///     repository of an entity whose assembly declares no boundary could not be constructed:
    ///     "Unable to resolve service for type 'DbContext'", at container validation, on a type the
    ///     author never wrote. A package is exactly that case, because it does not know which
    ///     application will import it, and the importing boundary bridges the unkeyed registration to
    ///     its own.
    /// </remarks>
    private string KeyedServiceAttribute() => KeyedServiceAttribute(_model);

    /// <inheritdoc cref="KeyedServiceAttribute()" />
    /// <remarks>Shared with the local identity store, which saves through the same unit of work.</remarks>
    internal static string KeyedServiceAttribute(EntityMetadataModel model)
    {
        if (string.IsNullOrEmpty(model.BoundaryTypeFullName))
            return "";

        var boundary = model.BoundaryTypeFullName!.StartsWith("global::", StringComparison.Ordinal)
            ? model.BoundaryTypeFullName
            : $"global::{model.BoundaryTypeFullName}";

        return $"[global::Microsoft.Extensions.DependencyInjection.FromKeyedServices(typeof({boundary}))] ";
    }

    /// <summary>
    ///     Renders the fourth rung: run a declared query through the repository the operation already
    ///     holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         ⚠️ <c>Set</c>, never <c>Query()</c>, and it is the whole correctness of this: the
    ///         executor prepares the source itself — <c>PrepareSource</c>, then <c>query.Apply</c>,
    ///         then <c>ApplyGlobalFilters</c> — while <c>Query()</c> is already <c>ApplyFilters(Set)</c>
    ///         and <c>ApplyFilters</c> calls <c>IgnoreQueryFilters</c>, of which EF keeps the last in a
    ///         chain. Handing a filtered source to something that filters again changes which filters
    ///         are active rather than repeating them, and the difference shows only under
    ///         <c>[FilterMode]</c> or <c>[WithoutFilter&lt;T&gt;]</c>.
    ///     </para>
    ///     <para>
    ///         No operation pipeline: the caller is inside one already. Stated on the interface, where
    ///         a reader choosing between this and the boundary facade will look.
    ///     </para>
    /// </remarks>
    private void RenderRunAsync(string entityType)
    {
        XmlSummary("Runs a declared query against this repository's own set.");
        XmlParam("query", "The declared query.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The rows the query answers with.");
        AppendLine($"public global::System.Threading.Tasks.Task<global::System.Collections.Generic.IReadOnlyList<TResult>> RunAsync<TResult>(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Persistence.Query.Interfaces.IQuery<{entityType}, TResult> query,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        AppendLine("where TResult : class");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("var executor = _executor ?? throw NoExecutor();");
            AppendLine("return executor.ExecuteAllAsync(query, Set, ct);");
        });

        AppendLine();

        XmlSummary("Runs a declared paged query against this repository's own set.");
        XmlParam("query", "The declared paged query.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The page the query declares.");
        AppendLine($"public global::System.Threading.Tasks.Task<global::Pragmatic.Persistence.Query.Results.PagedResult<TResult>> RunAsync<TResult>(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Persistence.Query.Interfaces.IPagedQuery<{entityType}, TResult> query,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        AppendLine("where TResult : class");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("var executor = _executor ?? throw NoExecutor();");
            AppendLine("return executor.ExecuteAsync(query, Set, ct);");
        });

        AppendLine();

        XmlSummary("Says what is missing when this repository was built without an executor.");
        AppendLine("private static global::System.InvalidOperationException NoExecutor()");
        IncreaseIndent();
        AppendLine("=> new(\"This repository was constructed without an IQueryExecutor, so it cannot run a \" +");
        AppendLine("       \"declared query. Resolve it from the container, which registers one, rather than \" +");
        AppendLine("       \"constructing the repository by hand.\");");
        DecreaseIndent();

        AppendLine();
    }

    private void RenderApplyFilters(string entityType)
    {
        XmlSummary("Applies global query filters (root-level and navigation-level) to the queryable.");
        AppendLine($"private global::System.Linq.IQueryable<{entityType}> ApplyFilters(global::System.Linq.IQueryable<{entityType}> query)");
        Block(() =>
        {
            Comment("Early exit if no filters configured (common in tests and simple apps)");
            AppendLine("if (_filterProvider is null && _filterMapComposer is null) return query;");
            AppendLine();
            Comment("Build shared FilterContext for consistent behavior across root and navigation filters");
            AppendLine("var context = BuildFilterContext();");
            AppendLine();

            // The tenant rule is enforced twice — here, and by the EF Core named query filter the
            // generated DbContext installs. FilterMode governed only this half, so
            // FilterMode.Background lifted one of two filters and a background job still read zero
            // rows. Named filters keep it surgical: Background drops Tenant and keeps SoftDelete, as
            // its own summary promises; Raw drops everything, which is what "no automatic filters"
            // has to mean when half of them live in EF's model.
            // A declared [VisibleWhen<TRule>] rule lives on the EF model too, and a write reaches its
            // entity through here rather than through the query executor: without this an operation
            // carrying [WithoutFilter<TRule>] would still fail to load the very row it exists to fix.
            // Gathered into ONE call because EF keeps the last IgnoreQueryFilters in the chain — two
            // calls would replace rather than accumulate.
            Comment("FilterMode and [WithoutFilter<TRule>] also govern the EF Core named query filters");
            AppendLine("if (context.IsRaw)");
            Block(() =>
            {
                AppendLine("query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query);");
            });
            AppendLine("else");
            Block(() =>
            {
                AppendLine("var __lifted = new global::System.Collections.Generic.List<string>(context.DisabledQueryFilterNames);");
                AppendLine("if (context.SkipTenant) __lifted.Add(\"Tenant\");");
                AppendLine("if (__lifted.Count > 0)");
                IncreaseIndent();
                AppendLine("query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(query, __lifted);");
                DecreaseIndent();
            });
            AppendLine();

            Comment("Root-level filters (IQueryFilterProvider — soft-delete, tenant, authorization)");
            Comment("Uses FilterContext-aware overload to respect FilterMode and DisabledFilters");
            AppendLine("if (_filterProvider is not null)");
            Block(() =>
            {
                AppendLine($"var filter = _filterProvider.GetCombinedFilter<{entityType}>(context, global::Pragmatic.Persistence.Query.Filters.NavigationContext.Root<{entityType}>());");
                AppendLine("if (filter is not null) query = query.Where(filter);");
            });

            AppendLine();
            Comment("Navigation-level filters (FilterMapComposer — filters on Include/ThenInclude navigations)");
            AppendLine("if (_filterMapComposer is not null)");
            Block(() =>
            {
                AppendLine($"query = _filterMapComposer.ApplyNavigationFilters(query, context);");
            });

            AppendLine();
            AppendLine("return query;");
        });

        AppendLine();

        RenderApplyRootFilters(entityType);

        AppendLine();

        // BuildFilterContext helper
        RenderBuildFilterContext();
    }

    private void RenderApplyRootFilters(string entityType)
    {
        XmlSummary("Applies only the root-level global query filters (soft-delete, tenant, ownership, scoped, authorization). Used by set-based bulk operations: they must not lose record-level security, but ExecuteUpdate/ExecuteDelete cannot carry navigation filters.");
        AppendLine($"private global::System.Linq.IQueryable<{entityType}> ApplyRootFilters(global::System.Linq.IQueryable<{entityType}> query)");
        Block(() =>
        {
            AppendLine("if (_filterProvider is null) return query;");
            AppendLine("var context = BuildFilterContext();");
            AppendLine($"var filter = _filterProvider.GetCombinedFilter<{entityType}>(context, global::Pragmatic.Persistence.Query.Filters.NavigationContext.Root<{entityType}>());");
            AppendLine("if (filter is not null) query = query.Where(filter);");
            AppendLine("return query;");
        });
    }

    private void RenderBuildFilterContext()
    {
        XmlSummary("Builds a FilterContext from the current scope state for navigation filtering.");
        AppendLine("private global::Pragmatic.Persistence.Query.Filters.FilterContext BuildFilterContext()");
        Block(() =>
        {
            AppendLine("return new global::Pragmatic.Persistence.Query.Filters.FilterContext");
            Block(() =>
            {
                AppendLine("TenantId = _tenantContext?.TenantId,");

                if (_model.IsSoftDelete || _model.IsAuditable)
                    AppendLine("UserId = _currentUser?.IdOrNull(),");

                // One source, whatever the entity declares. A different clock in each arm of that
                // branch — the injected one for an auditable entity and DateTimeOffset.UtcNow for
                // everything else — would make whether a read can be pinned depend on which traits
                // the entity happens to carry.
                AppendLine("Now = _timeProvider.GetUtcNow(),");

                AppendLine("Mode = _filterToggle?.CurrentMode ?? global::Pragmatic.Persistence.Query.Filters.FilterMode.Normal,");
                AppendLine("DisabledFilters = _filterToggle?.GetDisabledFilterTypes() ?? new global::System.Collections.Generic.HashSet<global::System.Type>(),");
            AppendLine("DisabledQueryFilterNames = _filterToggle?.GetDisabledQueryFilterNames() ?? new global::System.Collections.Generic.HashSet<string>(),");
            });
            AppendLine(";");
        });
    }

    private void RenderGetByIdAsync(string entityType, string idType)
    {
        XmlSummary($"Gets a {_model.TypeName} by its primary key. Global query filters are applied.");
        XmlParam("id", "The entity's primary key.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns($"The entity if found (and not filtered), null otherwise.");

        var parameters = new List<MethodParameter>
        {
            new(idType, "id"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("GetByIdAsync", () =>
        {
            AppendLine($"return ApplyFilters(Set).FirstOrDefaultAsync(e => e.PersistenceId.Equals(id), ct);");
        }, $"global::System.Threading.Tasks.Task<{entityType}?>", parameters);
    }

    private void RenderGetByIdWithIncludesAsync(string entityType, string idType)
    {
        XmlSummary($"Gets a {_model.TypeName} by its primary key with specified includes. Global query filters are applied.");
        XmlParam("id", "The entity's primary key.");
        XmlParam("includes", "Functions to specify includes.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns($"The entity if found (and not filtered), null otherwise.");

        AppendLine($"public global::System.Threading.Tasks.Task<{entityType}?> GetByIdAsync(");
        IncreaseIndent();
        AppendLine($"{idType} id,");
        AppendLine($"global::System.Func<global::System.Linq.IQueryable<{entityType}>, global::System.Linq.IQueryable<{entityType}>> includes,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            AppendLine("var query = includes(ApplyFilters(Set));");
            AppendLine("return query.FirstOrDefaultAsync(e => e.PersistenceId.Equals(id), ct);");
        });
    }

    private void RenderAdd(string entityType)
    {
        XmlSummary($"Adds a new {_model.TypeName} to the context.");
        XmlParam("entity", "The entity to add.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "entity")
        };

        Method("Add", () =>
        {
            AppendLine("_db.Add(entity);");
        }, "void", parameters);
    }

    private void RenderAddRange(string entityType)
    {
        XmlSummary($"Adds multiple {_model.TypeName} entities to the context.");
        XmlParam("entities", "The entities to add.");

        var parameters = new List<MethodParameter>
        {
            new($"global::System.Collections.Generic.IEnumerable<{entityType}>", "entities")
        };

        Method("AddRange", () =>
        {
            AppendLine("_db.AddRange(entities);");
        }, "void", parameters);
    }

    private void RenderRemove(string entityType)
    {
        XmlSummary($"Removes a {_model.TypeName} from the context.");
        XmlParam("entity", "The entity to remove.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "entity")
        };

        Method("Remove", () =>
        {
            if (_model.IsSoftDelete)
            {
                Comment("Soft delete - mark as deleted instead of removing");
                AppendLine("entity.IsDeleted = true;");
                AppendLine("entity.DeletedAt = _timeProvider.GetUtcNow();");
                AppendLine("entity.DeletedBy = _currentUser?.IdOrNull();");
            }
            else
            {
                AppendLine("_db.Remove(entity);");
            }
        }, "void", parameters);
    }

    private void RenderRemoveRange(string entityType)
    {
        XmlSummary($"Removes multiple {_model.TypeName} entities from the context.");
        XmlParam("entities", "The entities to remove.");

        var parameters = new List<MethodParameter>
        {
            new($"global::System.Collections.Generic.IEnumerable<{entityType}>", "entities")
        };

        Method("RemoveRange", () =>
        {
            if (_model.IsSoftDelete)
            {
                Comment("Soft delete - mark as deleted instead of removing");
                AppendLine("foreach (var entity in entities)");
                Block(() =>
                {
                    AppendLine("entity.IsDeleted = true;");
                    AppendLine("entity.DeletedAt = _timeProvider.GetUtcNow();");
                    AppendLine("entity.DeletedBy = _currentUser?.IdOrNull();");
                });
            }
            else
            {
                AppendLine("_db.RemoveRange(entities);");
            }
        }, "void", parameters);
    }

    private void RenderUpdate(string entityType)
    {
        XmlSummary($"Marks a {_model.TypeName} as modified in the context.");
        XmlParam("entity", "The entity to update.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "entity")
        };

        Method("Update", () =>
        {
            AppendLine("_db.Update(entity);");
        }, "void", parameters);
    }

    private void RenderFindAsync(string entityType)
    {
        XmlSummary($"Finds {_model.TypeName} entities matching a specification.");
        XmlParam("spec", "The specification to match.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("A list of matching entities.");

        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Specification.ISpecification<{entityType}>", "spec"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("FindAsync", () =>
        {
            AppendLine("return ApplyFilters(Set).Where(spec.ToExpression()).ToListAsync(ct);");
        }, $"global::System.Threading.Tasks.Task<global::System.Collections.Generic.List<{entityType}>>", parameters);
    }

    private void RenderCountAsync(string entityType)
    {
        XmlSummary($"Counts {_model.TypeName} entities matching a specification.");
        XmlParam("spec", "The specification to match.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The count of matching entities.");

        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Specification.ISpecification<{entityType}>", "spec"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("CountAsync", () =>
        {
            AppendLine("return ApplyFilters(Set).Where(spec.ToExpression()).CountAsync(ct);");
        }, "global::System.Threading.Tasks.Task<int>", parameters);
    }

    private void RenderExistsAsync(string entityType)
    {
        XmlSummary($"Checks if any {_model.TypeName} entity matches a specification.");
        XmlParam("spec", "The specification to match.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("True if any entity matches; otherwise, false.");

        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Specification.ISpecification<{entityType}>", "spec"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("ExistsAsync", () =>
        {
            AppendLine("return ApplyFilters(Set).Where(spec.ToExpression()).AnyAsync(ct);");
        }, "global::System.Threading.Tasks.Task<bool>", parameters);
    }

    private void RenderFirstOrDefaultAsync(string entityType)
    {
        XmlSummary($"Gets the first {_model.TypeName} matching a specification, or null.");
        XmlParam("spec", "The specification to match.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The first matching entity or null.");

        var parameters = new List<MethodParameter>
        {
            new($"global::Pragmatic.Specification.ISpecification<{entityType}>", "spec"),
            new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
        };

        Method("FirstOrDefaultAsync", () =>
        {
            AppendLine("return ApplyFilters(Set).Where(spec.ToExpression()).FirstOrDefaultAsync(ct);");
        }, $"global::System.Threading.Tasks.Task<{entityType}?>", parameters);
    }

    private void RenderQuery(string entityType)
    {
        XmlSummary($"Gets a queryable for {_model.TypeName} for advanced query scenarios. Uses Filtered strategy (filters applied, tracking enabled).");
        XmlReturns("An IQueryable for the entity type.");

        ExpressionMethod("Query", $"Query(global::Pragmatic.Persistence.Query.QueryStrategy.Filtered)", $"global::System.Linq.IQueryable<{entityType}>");

        AppendLine();
        RenderEnsureLoaded(entityType);
        RenderLinkNavigation(entityType);

        AppendLine();

        // Strategy-based overload
        XmlSummary($"Gets a queryable for {_model.TypeName} with the specified query strategy.");
        XmlParam("strategy", "The query strategy (Projection, Entity, Filtered, Raw).");
        XmlReturns("An IQueryable configured according to the strategy.");

        var parameters = new List<MethodParameter>
        {
            new("global::Pragmatic.Persistence.Query.QueryStrategy", "strategy")
        };

        Method("Query", () =>
        {
            AppendLine("return strategy switch");
            Block(() =>
            {
                AppendLine($"global::Pragmatic.Persistence.Query.QueryStrategy.Projection => ApplyFilters(Set.AsNoTracking()),");
                AppendLine($"global::Pragmatic.Persistence.Query.QueryStrategy.Entity => ApplyFilters(Set),");
                AppendLine($"global::Pragmatic.Persistence.Query.QueryStrategy.Filtered => ApplyFilters(Set),");
                AppendLine($"global::Pragmatic.Persistence.Query.QueryStrategy.Raw => Set.AsNoTracking().AsQueryable(),");
                AppendLine($"_ => ApplyFilters(Set),");
            });
            AppendLine(";");
        }, $"global::System.Linq.IQueryable<{entityType}>", parameters);
    }

    /// <summary>
    ///     The lookup by the entity's domain key.
    /// </summary>
    /// <remarks>
    ///     One part keeps the name and the signature it has always had — <c>GetByCodeAsync(code)</c>.
    ///     More than one becomes <c>GetByCodeAndSeasonAsync(code, season)</c>: naming every part is what
    ///     makes the call site say which key it is addressing, and a domain key of two columns has no
    ///     single "the key value" to pass.
    /// </remarks>
    /// <summary>
    ///     Loads only the navigation paths the entity is missing — <c>INavigationLoader</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Three costs, and the common one is free. The planner reads the change tracker, which
    ///         emits no SQL, so a graph that is already complete costs <b>zero</b> round trips;
    ///         anything missing costs <b>one</b> read with exactly those includes, never one per row.
    ///     </para>
    ///     <para>
    ///         The result is discarded on purpose: what matters is EF's fix-up onto the instance
    ///         already tracked, which is the one the caller will write.
    ///     </para>
    ///     <para>
    ///         ⚠️ A detached entity is attached first — writing into a graph the context does not
    ///         track never reaches <c>SaveChanges</c>. Attaching does not make the caller's children
    ///         authoritative: EF does not set <c>IsLoaded</c> on an attach, so the planner still
    ///         reports them missing and the read happens. Measured, not assumed —
    ///         <c>NavigationLoadPlannerTests.AfterAttach_APopulatedCollection_IsStillReportedAsMissing</c>.
    ///     </para>
    /// </remarks>
    /// <summary>
    ///     Chooses which rows a navigation points at, from their keys — <c>INavigationLinker</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         One read at most, and usually none: the links already tracked are read from the change
    ///         tracker, and a row named by a key it does not know is <b>attached as a stub</b> rather
    ///         than loaded. Writing a join row needs the key and nothing else.
    ///     </para>
    ///     <para>
    ///         ⚠️ The navigation still has to be loaded, and for the usual reason: a merge decides what
    ///         to remove by looking at what is there, so against an unloaded collection it removes
    ///         nothing and links everything again. The helper refuses rather than guesses.
    ///     </para>
    /// </remarks>
    private void RenderLinkNavigation(string entityType)
    {
        XmlSummary("Makes the given keys the links of a navigation.");
        XmlReturns("0 when nothing had to be read.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "entity"),
            new("string", "navigation"),
            new("global::System.Collections.Generic.IReadOnlyList<object>", "ids"),
            new("string", "key"),
            new("string", "strategy"),
            new("global::System.Func<TRelated>", "stubFactory"),
            new("global::System.Threading.CancellationToken", "ct")
        };

        GenericMethod("LinkAsync", ["TRelated"], () =>
        {
            AppendLine("if (_db.Entry(entity).State == global::Microsoft.EntityFrameworkCore.EntityState.Detached)");
            Block(() => AppendLine("_db.Attach(entity);"));
            AppendLine();
            AppendLine("var entry = _db.Entry(entity).Navigation(navigation);");
            AppendLine("var reads = 0;");
            AppendLine("if (!entry.IsLoaded)");
            Block(() =>
            {
                AppendLine("await entry.LoadAsync(ct).ConfigureAwait(false);");
                AppendLine("reads = 1;");
            });
            AppendLine();
            // Conditionals, not Enum.Parse: parsing an enum by name is dynamic-code work the AOT
            // publish flags, over a set of four that is known here at generation time.
            const string Strategy = "global::Pragmatic.Mapping.Mutation.CollectionStrategy";
            AppendLine($"var chosen = strategy == \"AddOnly\" ? {Strategy}.AddOnly");
            IncreaseIndent();
            AppendLine($": strategy == \"Replace\" ? {Strategy}.Replace");
            AppendLine($": strategy == \"Ignore\" ? {Strategy}.Ignore");
            AppendLine($": {Strategy}.Sync;");
            DecreaseIndent();
            AppendLine();
            AppendLine(
                "global::Pragmatic.Mapping.EFCore.Mutation.EfMutationHelpers.MapIdsToMany(");
            IncreaseIndent();
            AppendLine("ids,");
            AppendLine("entry,");
            AppendLine("key,");
            AppendLine("stubFactory,");
            AppendLine("chosen);");
            DecreaseIndent();
            AppendLine();
            AppendLine("return reads;");
        },
        "async global::System.Threading.Tasks.Task<int>",
        parameters,
        constraints: "TRelated : class");

        AppendLine();
    }

    private void RenderEnsureLoaded(string entityType)
    {
        XmlSummary("Loads only the navigation paths this entity is missing.");
        XmlReturns("0 when nothing had to be read, 1 otherwise.");

        var parameters = new List<MethodParameter>
        {
            new(entityType, "entity"),
            new("global::System.Collections.Generic.IReadOnlyList<string>", "paths"),
            new("global::System.Threading.CancellationToken", "ct")
        };

        Method("EnsureLoadedAsync", () =>
        {
            AppendLine(
                "var missing = global::Pragmatic.Persistence.EFCore.Repository.NavigationLoadPlanner"
                + ".MissingPaths(_db, entity, paths);");
            AppendLine("if (missing.Count == 0)");
            Block(() => AppendLine("return 0;"));
            AppendLine();
            AppendLine("if (_db.Entry(entity).State == global::Microsoft.EntityFrameworkCore.EntityState.Detached)");
            Block(() => AppendLine("_db.Attach(entity);"));
            AppendLine();
            AppendLine("var query = Query();");
            AppendLine("foreach (var path in missing)");
            Block(() => AppendLine(
                "query = global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                + ".Include(query, path);"));
            AppendLine();
            Comment("Discarded: what matters is the fix-up onto the tracked instance, not the value.");
            AppendLine(
                "_ = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions"
                + ".FirstOrDefaultAsync(query, e => e.PersistenceId.Equals(entity.PersistenceId), ct)"
                + ".ConfigureAwait(false);");
            AppendLine();
            AppendLine("return 1;");
        },
        "async global::System.Threading.Tasks.Task<int>",
        parameters,
        AccessModifier.Public);
    }

    private void RenderGetByLogicKey(string entityType)
    {
        var keys = _model.LogicKeys.AsImmutableArray();
        var methodName = $"GetBy{string.Join("And", keys.Select(k => k.Name))}Async";

        XmlSummary($"Gets a {_model.TypeName} by its domain key ({string.Join(", ", keys.Select(k => k.Name))}).");
        foreach (var key in keys)
            XmlParam(TemplateHelpers.ToCamelCase(key.Name), $"The {key.Name} part of the domain key.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The entity if found, null otherwise.");

        var parameters = keys
            .Select(k => new MethodParameter(NormalizeTypeName(k.TypeName), TemplateHelpers.ToCamelCase(k.Name)))
            .ToList();
        parameters.Add(new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" });

        var predicate = string.Join(" && ", keys.Select(k => $"e.{k.Name} == {TemplateHelpers.ToCamelCase(k.Name)}"));

        Method(methodName, () =>
        {
            AppendLine($"return ApplyFilters(Set).FirstOrDefaultAsync(e => {predicate}, ct);");
        }, $"global::System.Threading.Tasks.Task<{entityType}?>", parameters);
    }

    private void RenderBulkUpdateAsync(string entityType)
    {
        XmlSummary($"Updates {_model.TypeName} entities matching the specification without loading them into memory. Translates to SQL UPDATE ... SET ... WHERE.");
        XmlParam("filter", "The specification to filter which entities to update.");
        XmlParam("updateAction", "An action describing which properties to update and their new values.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The number of entities updated.");

        var asyncModifier = _model.IsAudited ? "async " : string.Empty;
        AppendLine($"public {asyncModifier}global::System.Threading.Tasks.Task<int> BulkUpdateAsync(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Specification.ISpecification<{entityType}> filter,");
        AppendLine($"global::System.Action<global::Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<{entityType}>> updateAction,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            if (_model.IsAudited)
            {
                AppendLine("var __affected = await ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteUpdateAsync(updateAction, ct).ConfigureAwait(false);");
                RenderBulkAuditRow("Data.EntityBulkUpdated");
                AppendLine("return __affected;");
            }
            else
            {
                AppendLine("return ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteUpdateAsync(updateAction, ct);");
            }
        });
    }

    // Writes one aggregate audit row after a bulk ExecuteUpdate/Delete on an [Audited] entity.
    // ExecuteUpdate/Delete bypass the change tracker, so AuditLogInterceptor never observes these rows;
    // without this the audit trail silently diverges from what was persisted. Staged into the
    // same context rather than written separately, so the aggregate entry commits with the bulk change.
    private void RenderBulkAuditRow(string action)
    {
        Comment("Synthetic bulk audit: ExecuteUpdate/Delete bypasses the change tracker, so the");
        Comment("AuditLogInterceptor never sees these rows. Record one aggregate [Audited] entry.");
        AppendLine("if (__affected > 0)");
        Block(() =>
        {
            AppendLine("global::Pragmatic.Audit.EFCore.AuditEntryStaging.Stage(_db, new global::Pragmatic.Audit.AuditEntry");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("SegmentId = string.Empty,");
            AppendLine("OccurredAt = _timeProvider.GetUtcNow(),");
            AppendLine("Category = global::Pragmatic.Audit.AuditCategory.Data,");
            AppendLine($"Operation = \"{action}\",");
            AppendLine("ActorRef = _currentUser?.IdOrNull(),");
            AppendLine("CorrelationId = global::System.Diagnostics.Activity.Current?.TraceId.ToString(),");
            AppendLine($"TargetType = \"{_model.TypeName}\",");
            AppendLine("TargetId = \"(bulk: \" + __affected.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" rows)\",");
            AppendLine("Outcome = global::Pragmatic.Audit.AuditOutcome.Success,");
            DecreaseIndent();
            AppendLine("}, new global::Pragmatic.Audit.AuditEntryPreparer(");
            IncreaseIndent();
            AppendLine("new global::Pragmatic.Audit.PatternAuditDetailRedactor(),");
            AppendLine("new global::Pragmatic.Audit.HourlyAuditSegmentNaming(),");
            AppendLine("_timeProvider));");
            DecreaseIndent();
            AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
        });
    }

    private void RenderBulkDeleteAsync(string entityType)
    {
        XmlSummary($"Deletes {_model.TypeName} entities matching the specification without loading them into memory.");
        XmlParam("filter", "The specification to filter which entities to delete.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The number of entities deleted.");

        if (_model.IsSoftDelete)
        {
            Comment("Soft-delete: sets IsDeleted, DeletedAt, DeletedBy instead of physical delete");
        }

        // Always async: a runtime roll-up participation check may route to the tracked path.
        AppendLine("public async global::System.Threading.Tasks.Task<int> BulkDeleteAsync(");
        IncreaseIndent();
        AppendLine($"global::Pragmatic.Specification.ISpecification<{entityType}> filter,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            // Roll-up children: ExecuteDelete/Update bypasses the RollUpInterceptor, so parent
            // aggregates would drift. Route through the tracked path so the interceptor observes each
            // change and recomputes aggregates (and writes correct per-row [Audited] entries).
            AppendLine("if (_participatesInRollUp)");
            Block(() =>
            {
                AppendLine("var __tracked = await ApplyRootFilters(Set).Where(filter.ToExpression()).ToListAsync(ct).ConfigureAwait(false);");
                AppendLine("if (__tracked.Count == 0) return 0;");
                if (_model.IsSoftDelete)
                {
                    AppendLine("var __deletedBy = _currentUser?.IdOrNull();");
                    AppendLine("var __now = _timeProvider.GetUtcNow();");
                    AppendLine("foreach (var __e in __tracked)");
                    Block(() =>
                    {
                        AppendLine("__e.IsDeleted = true;");
                        AppendLine("__e.DeletedAt = __now;");
                        AppendLine("__e.DeletedBy = __deletedBy;");
                    });
                }
                else
                {
                    AppendLine("_db.RemoveRange(__tracked);");
                }

                AppendLine("await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
                AppendLine("return __tracked.Count;");
            });
            AppendLine();

            // Fast path (no roll-up): SQL ExecuteUpdate/Delete, plus a synthetic aggregate audit row when [Audited].
            if (_model.IsSoftDelete)
            {
                Comment("Soft delete - mark as deleted instead of physically removing");
                AppendLine("var deletedBy = _currentUser?.IdOrNull();");
                AppendLine("var now = _timeProvider.GetUtcNow();");
                AppendLine("var __affected = await ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteUpdateAsync(s =>");
                AppendLine("{");
                IncreaseIndent();
                AppendLine("s.SetProperty(e => e.IsDeleted, true);");
                AppendLine("s.SetProperty(e => e.DeletedAt, now);");
                AppendLine("s.SetProperty(e => e.DeletedBy, deletedBy);");
                DecreaseIndent();
                AppendLine("}, ct).ConfigureAwait(false);");
            }
            else
            {
                AppendLine("var __affected = await ApplyRootFilters(Set).Where(filter.ToExpression()).ExecuteDeleteAsync(ct).ConfigureAwait(false);");
            }

            if (_model.IsAudited)
                RenderBulkAuditRow("Data.EntityBulkDeleted");
            AppendLine("return __affected;");
        });
    }

    private void RenderBulkDescriptor(string entityType)
    {
        Comment("Source-generated descriptor for bulk operations — zero reflection property access");
        AppendLine($"private static readonly global::Pragmatic.Persistence.EFCore.Bulk.BulkEntityDescriptor<{entityType}> _bulkDescriptor = new()");
        Block(() =>
        {
            // Column roles array
            AppendLine("Columns = new (string, global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole)[]");
            Block(() =>
            {
                var auditProps = new HashSet<string> { "CreatedAt", "CreatedBy" };
                var updateOnlyProps = new HashSet<string> { "UpdatedAt", "UpdatedBy" };
                var softDeleteProps = new HashSet<string> { "IsDeleted", "DeletedAt", "DeletedBy" };
                var concurrencyProps = new HashSet<string> { "RowVersion" };

                foreach (var prop in _model.Properties)
                {
                    string role;
                    if (prop.Name == "PersistenceId")
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.Key";
                    else if (prop.IsLogicKey)
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.LogicKey";
                    else if (_model.IsAuditable && auditProps.Contains(prop.Name))
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.InsertOnly";
                    else if (_model.IsAuditable && updateOnlyProps.Contains(prop.Name))
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.UpdateOnly";
                    else if (_model.IsSoftDelete && softDeleteProps.Contains(prop.Name))
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.SoftDelete";
                    else if (_model.IsConcurrencyAware && _provider != EfCoreProvider.PostgreSql && concurrencyProps.Contains(prop.Name))
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.Computed";
                    else
                        role = "global::Pragmatic.Persistence.EFCore.Bulk.BulkColumnRole.Regular";

                    AppendLine($"(\"{prop.Name}\", {role}),");
                }
            });
            AppendLine(",");

            // ReadValue delegate
            AppendLine($"ReadValue = static (entity, prop) => prop switch");
            Block(() =>
            {
                foreach (var prop in _model.Properties)
                {
                    // An enum is read as the int it is stored as — and a nullable one as a nullable int:
                    // `(int)` on a Nullable<TEnum> is CS8629 in a file the author cannot edit, which is
                    // what a column meaning "no value yet" would produce.
                    if (prop.IsEnum)
                        AppendLine($"\"{prop.Name}\" => ({(prop.IsNullable ? "int?" : "int")})entity.{prop.Name},");
                    else if (prop.IsNullable)
                        AppendLine($"\"{prop.Name}\" => (object?)entity.{prop.Name},");
                    else
                        AppendLine($"\"{prop.Name}\" => entity.{prop.Name},");
                }
                AppendLine("_ => null,");
            });
            AppendLine(",");
        });
        AppendLine(";");
    }

    private void RenderBulkInsertAsync(string entityType)
    {
        XmlSummary($"Inserts multiple {_model.TypeName} entities in batches using multi-row INSERT VALUES via ADO.NET. Bypasses the change tracker.");
        XmlParam("entities", "The entities to insert.");
        XmlParam("options", "Batch size and timeout options. Null for defaults.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The total number of rows inserted.");

        AppendLine($"public global::System.Threading.Tasks.Task<int> BulkInsertAsync(");
        IncreaseIndent();
        AppendLine($"global::System.Collections.Generic.IReadOnlyList<{entityType}> entities,");
        AppendLine("global::Pragmatic.Persistence.EFCore.Bulk.BulkInsertOptions? options = null,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            if (_model.IsAuditable || _model.IsSoftDelete)
            {
                AppendLine("var now = _timeProvider.GetUtcNow();");
                AppendLine("var userId = _currentUser?.IdOrNull();");
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.InsertAsync(_db, entities, _bulkDescriptor, now, userId, options, ct);");
            }
            else
            {
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.InsertAsync(_db, entities, _bulkDescriptor, null, null, options, ct);");
            }
        });

        AppendLine();

        // Convenience overload without options
        XmlSummary($"Inserts multiple {_model.TypeName} entities in batches with default options.");
        AppendLine($"public global::System.Threading.Tasks.Task<int> BulkInsertAsync(");
        IncreaseIndent();
        AppendLine($"global::System.Collections.Generic.IReadOnlyList<{entityType}> entities,");
        AppendLine("global::System.Threading.CancellationToken ct)");
        DecreaseIndent();
        AppendLine($"    => BulkInsertAsync(entities, null, ct);");
    }

    private void RenderBulkUpsertAsync(string entityType)
    {
        XmlSummary($"Upserts (insert-or-update) multiple {_model.TypeName} entities in batches using provider-specific SQL.");
        XmlParam("entities", "The entities to upsert.");
        XmlParam("options", "Match strategy, batch size, and timeout. Null for defaults (PrimaryKey match).");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The total number of rows affected.");

        AppendLine($"public global::System.Threading.Tasks.Task<int> BulkUpsertAsync(");
        IncreaseIndent();
        AppendLine($"global::System.Collections.Generic.IReadOnlyList<{entityType}> entities,");
        AppendLine("global::Pragmatic.Persistence.EFCore.Bulk.UpsertOptions? options = null,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            if (_model.IsAuditable || _model.IsSoftDelete)
            {
                AppendLine("var now = _timeProvider.GetUtcNow();");
                AppendLine("var userId = _currentUser?.IdOrNull();");
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.UpsertAsync(_db, entities, _bulkDescriptor, now, userId, options, ct);");
            }
            else
            {
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.UpsertAsync(_db, entities, _bulkDescriptor, null, null, options, ct);");
            }
        });

        AppendLine();

        // Convenience overload
        XmlSummary($"Upserts multiple {_model.TypeName} entities with default options.");
        AppendLine($"public global::System.Threading.Tasks.Task<int> BulkUpsertAsync(");
        IncreaseIndent();
        AppendLine($"global::System.Collections.Generic.IReadOnlyList<{entityType}> entities,");
        AppendLine("global::System.Threading.CancellationToken ct)");
        DecreaseIndent();
        AppendLine($"    => BulkUpsertAsync(entities, null, ct);");
    }

    private void RenderUpsertAsync(string entityType)
    {
        XmlSummary($"Upserts a single {_model.TypeName} entity using provider-specific SQL (MERGE / ON CONFLICT).");
        XmlParam("entity", "The entity to upsert.");
        XmlParam("matchOn", "Strategy for matching existing entities. Default is PrimaryKey.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The number of rows affected.");

        AppendLine($"public global::System.Threading.Tasks.Task<int> UpsertAsync(");
        IncreaseIndent();
        AppendLine($"{entityType} entity,");
        AppendLine("global::Pragmatic.Persistence.EFCore.Bulk.UpsertMatch matchOn = global::Pragmatic.Persistence.EFCore.Bulk.UpsertMatch.PrimaryKey,");
        AppendLine("global::System.Threading.CancellationToken ct = default)");
        DecreaseIndent();
        Block(() =>
        {
            if (_model.IsAuditable || _model.IsSoftDelete)
            {
                AppendLine("var now = _timeProvider.GetUtcNow();");
                AppendLine("var userId = _currentUser?.IdOrNull();");
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.UpsertSingleAsync(_db, entity, _bulkDescriptor, now, userId, matchOn, ct);");
            }
            else
            {
                AppendLine("return global::Pragmatic.Persistence.EFCore.Bulk.BulkExecutor.UpsertSingleAsync(_db, entity, _bulkDescriptor, null, null, matchOn, ct);");
            }
        });

        AppendLine();

        // Convenience overload
        XmlSummary($"Upserts a single {_model.TypeName} entity matching on PrimaryKey.");
        AppendLine($"public global::System.Threading.Tasks.Task<int> UpsertAsync(");
        IncreaseIndent();
        AppendLine($"{entityType} entity,");
        AppendLine("global::System.Threading.CancellationToken ct)");
        DecreaseIndent();
        AppendLine($"    => UpsertAsync(entity, global::Pragmatic.Persistence.EFCore.Bulk.UpsertMatch.PrimaryKey, ct);");
    }

    private void RenderSaveChangesAsync()
    {
        if (_model.IsConcurrencyAware)
        {
            RenderSaveChangesAsyncWithConcurrency();
        }
        else
        {
            XmlSummary("Saves all changes to the database.");
            XmlParam("ct", "Cancellation token.");
            XmlReturns("The number of entities written to the database.");

            var parameters = new List<MethodParameter>
            {
                new("global::System.Threading.CancellationToken", "ct") { DefaultValue = "default" }
            };

            Method("SaveChangesAsync", () =>
            {
                AppendLine("return _unitOfWork.SaveChangesAsync(ct);");
            }, "global::System.Threading.Tasks.Task<int>", parameters);
        }
    }

    private void RenderSaveChangesAsyncWithConcurrency()
    {
        XmlSummary("Saves all changes to the database. Returns a ConcurrencyError if a concurrency conflict is detected.");
        XmlParam("ct", "Cancellation token.");
        XmlReturns("The number of entities written, or a ConcurrencyError on conflict.");

        AppendLine("public async global::System.Threading.Tasks.Task<global::Pragmatic.Result.Result<int, global::Pragmatic.Persistence.Repository.ConcurrencyError>> SaveChangesAsync(global::System.Threading.CancellationToken ct = default)");
        Block(() =>
        {
            AppendLine("try");
            Block(() =>
            {
                AppendLine("var result = await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);");
                AppendLine("return result;");
            });
            // No binding: nothing here reads the exception, and naming it costs every consumer a CS0168
            // in a file they did not write and cannot edit. A generator's warnings are spent from the
            // application's budget, which is how a project ends up unable to turn warnings into errors.
            AppendLine("catch (global::Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)");
            Block(() =>
            {
                AppendLine("return new global::Pragmatic.Persistence.Repository.ConcurrencyError");
                Block(() =>
                {
                    AppendLine($"Message = \"A concurrency conflict occurred while saving {_model.TypeName}. The entity was modified by another process.\",");
                    AppendLine($"EntityTypeName = \"{_model.FullTypeName}\"");
                });
                AppendLine(";");
            });
        });
    }

    private string GetFullIdType()
    {
        return _model.IdType switch
        {
            "Guid" => "global::System.Guid",
            "int" => "int",
            "long" => "long",
            "string" => "string",
            _ when _model.IdType.Contains(".") => $"global::{_model.IdType}",
            _ => _model.IdType
        };
    }

    private static AccessModifier ParseAccessibility(string accessibility)
    {
        return accessibility.ToLowerInvariant() switch
        {
            "public" => AccessModifier.Public,
            "internal" => AccessModifier.Internal,
            "protected" => AccessModifier.Protected,
            "private" => AccessModifier.Private,
            _ => AccessModifier.Public
        };
    }

    /// <summary>
    ///     Normalizes type name for code generation.
    ///     Primitive C# aliases are kept as-is, while other types get global:: prefix.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        // C# primitive type aliases should not have global:: prefix
        return typeName switch
        {
            "string" or "String" or "System.String" => "string",
            "int" or "Int32" or "System.Int32" => "int",
            "long" or "Int64" or "System.Int64" => "long",
            "short" or "Int16" or "System.Int16" => "short",
            "byte" or "Byte" or "System.Byte" => "byte",
            "sbyte" or "SByte" or "System.SByte" => "sbyte",
            "uint" or "UInt32" or "System.UInt32" => "uint",
            "ulong" or "UInt64" or "System.UInt64" => "ulong",
            "ushort" or "UInt16" or "System.UInt16" => "ushort",
            "float" or "Single" or "System.Single" => "float",
            "double" or "Double" or "System.Double" => "double",
            "decimal" or "Decimal" or "System.Decimal" => "decimal",
            "bool" or "Boolean" or "System.Boolean" => "bool",
            "char" or "Char" or "System.Char" => "char",
            "object" or "Object" or "System.Object" => "object",
            "Guid" or "System.Guid" => "global::System.Guid",
            _ when typeName.Contains(".") => $"global::{typeName}",
            _ => typeName
        };
    }
}

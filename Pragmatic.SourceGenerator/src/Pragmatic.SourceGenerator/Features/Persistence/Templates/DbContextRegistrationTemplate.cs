using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Templates;

/// <summary>
///     Template for generating DbContext DI registration extension methods.
///     Uses typed keyed DI (typeof) instead of string keys for type safety.
/// </summary>
internal sealed class DbContextRegistrationTemplate : CSharpTemplate
{
    private readonly ImmutableArray<BoundaryDbContextModel> _boundaries;
    private readonly string _namespacePrefix;
    private readonly bool _hasMultiTenancy;
    private readonly bool _hasEvents;
    private readonly bool _hasTemporalEfCore;

    public DbContextRegistrationTemplate(
        ImmutableArray<BoundaryDbContextModel> boundaries,
        string namespacePrefix,
        bool hasMultiTenancy = false,
        bool hasEvents = false,
        bool hasTemporalEfCore = false)
    {
        _boundaries = boundaries;
        _namespacePrefix = namespacePrefix;
        _hasMultiTenancy = hasMultiTenancy;
        _hasEvents = hasEvents;
        _hasTemporalEfCore = hasTemporalEfCore;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Persistence";

    public override Artifact RenderOutput()
    {
        return new Artifact(
            VirtualFolderHints.ForAssembly("Persistence", "DbContextRegistration"),
            ToSourceText());
    }

    protected override bool Validate()
    {
        return _boundaries.Length > 0;
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");

        // Add usings for all boundary namespaces
        foreach (var boundary in _boundaries)
        {
            if (!string.IsNullOrEmpty(boundary.Namespace))
                AddUsing(boundary.Namespace);

            // Add using for boundary marker type
            if (!string.IsNullOrEmpty(boundary.BoundaryTypeName))
            {
                var boundaryNs = GetNamespace(boundary.BoundaryTypeName!);
                if (!string.IsNullOrEmpty(boundaryNs))
                    AddUsing(boundaryNs);
            }
        }

        AppendNamespace(NamespaceFor(_namespacePrefix));
        AppendLine();

        RenderClass();
    }

    private static string GetNamespace(string fullTypeName)
    {
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

    /// <summary>
    ///     The class as the host names it: fully qualified, because the host's generated code lives in a
    ///     namespace named after its own assembly, which need not enclose this one.
    /// </summary>
    internal static string QualifiedClassFor(string namespacePrefix)
        => $"global::{NamespaceFor(namespacePrefix)}.{ClassName}";

    private const string ClassName = "DbContextRegistrationExtensions";

    private static string NamespaceFor(string namespacePrefix)
        => string.IsNullOrEmpty(namespacePrefix) ? "Pragmatic.Persistence" : namespacePrefix;

    private void RenderClass()
    {
        XmlSummary("Extension methods for registering DbContexts with typed keyed DI.");
        Class(ClassName, RenderMethods,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true, Partial = true });
    }

    private void RenderMethods()
    {
        var isFirst = true;
        foreach (var boundary in _boundaries.Where(b => !b.IsMigrationContext))
        {
            if (!isFirst)
                AppendLine();
            isFirst = false;

            RenderBoundaryMethod(boundary);
        }

        // Aggregator method to register all boundaries at once
        if (_boundaries.Any(b => !b.IsMigrationContext))
        {
            AppendLine();
            RenderAddAllDbContextsMethod();
        }
    }

    private void RenderBoundaryMethod(BoundaryDbContextModel boundary)
    {
        XmlSummary($"Registers the {boundary.ClassName} with keyed DI for the {boundary.BoundaryName} boundary.");
        XmlParam("services", "The service collection.");
        XmlParam("configure", "Configuration action for the DbContext options.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services"),
            new("Action<DbContextOptionsBuilder>", "configure")
        };

        Method($"Add{boundary.BoundaryName}DbContext", () =>
        {
            // Register with (sp, options) overload to auto-wire interceptors from DI
            AppendLine($"services.AddDbContext<{boundary.ClassName}>((sp, options) =>");
            AppendLine("{");
            IncreaseIndent();
            AppendLine("configure(options);");
            AppendLine();

            if (_hasTemporalEfCore)
            {
                // Referencing the package is the opt-in; calling it is not the caller's job. The rest of
                // the framework reads a stored instant as UTC, and this is what makes that true. It
                // composes on whatever `configure` already set, so an application that called
                // UsePragmaticTemporal itself keeps its own options.
                AppendLine("global::Pragmatic.Temporal.EntityFrameworkCore.DbContextOptionsExtensions.UsePragmaticTemporal(options);");
                AppendLine();
            }

            if (boundary.AppliesBoundaryConfiguration && !string.IsNullOrEmpty(boundary.BoundaryTypeName))
            {
                // What the application passed to AddBoundary<T>(cfg => cfg.UseDatabase(...)), applied last so
                // it can change what the generated call set — the retry included. A provider call in it without a connection string keeps
                // the one `configure` set: EF's UseNpgsql/UseSqlServer update the existing extension.
                var boundaryType = boundary.BoundaryTypeName!.StartsWith("global::", StringComparison.Ordinal)
                    ? boundary.BoundaryTypeName
                    : $"global::{boundary.BoundaryTypeName}";
                AppendLine($"if (sp.GetService<global::Pragmatic.Actions.Boundary.BoundaryConfiguration<{boundaryType}>>()?.DatabaseOptions");
                AppendLine("    is global::System.Action<global::Microsoft.EntityFrameworkCore.DbContextOptionsBuilder> useDatabase)");
                AppendLine("    useDatabase(options);");
                AppendLine();
            }

            Comment("Auto-wire EF Core interceptors (provider-agnostic)");
            AppendLine("options.AddInterceptors(");
            IncreaseIndent();
            AppendLine("new global::Pragmatic.Persistence.EFCore.Interceptors.AuditingInterceptor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::System.TimeProvider>() ?? global::System.TimeProvider.System,");
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>()),");
            DecreaseIndent();
            // The repository's Remove already writes the flag; this is for every path that does not go
            // through it — a child severed from its parent's collection, a hand-written context.Remove.
            // [SoftDelete] promises a row is not permanently deleted and drew no such distinction, so
            // the guarantee is enforced where the paths converge. It never re-stamps an entity already
            // flagged: a cascade shares one instant, and restore brings back only what carries it.
            AppendLine("new global::Pragmatic.Persistence.EFCore.Interceptors.SoftDeleteInterceptor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::System.TimeProvider>() ?? global::System.TimeProvider.System,");
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>()),");
            DecreaseIndent();
            // Beside auditing, and for the same reason: an owner has to be stamped on every write path,
            // not only the one that goes through a mutation invoker. Without it a row created by an
            // action through a repository reached the database unowned, and the ownership filter then
            // hid it from everybody but a caller holding the bypass permission.
            AppendLine("new global::Pragmatic.Persistence.EFCore.Interceptors.OwnershipInterceptor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>()),");
            DecreaseIndent();
            // And beside ownership, for the same reason once more: [HasAccessScopes] generated a column,
            // a Grant/Revoke pair and a filter, and nothing ever wrote the column — so that filter was
            // false for every row and every caller, and a scoped row was invisible to the person who had
            // just created it. Ownership got its interceptor; scopes never had one.
            AppendLine("new global::Pragmatic.Persistence.EFCore.Interceptors.ScopeInterceptor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>(),");
            // The stamp and the rules run in one interceptor, in that order — see ScopeInterceptor:
            // the stamp writes only when the list is empty, so materializing first would make "empty"
            // never true again and no row would carry its creator's scope.
            AppendLine("sp.GetServices<global::Pragmatic.Persistence.EFCore.Scopes.IScopeMaterializationStep>()),");
            DecreaseIndent();
            if (_hasMultiTenancy)
            {
                AppendLine("new global::Pragmatic.Persistence.EFCore.Interceptors.TenantInterceptor(");
                IncreaseIndent();
                AppendLine("sp.GetRequiredService<global::Pragmatic.MultiTenancy.ITenantContext>(),");
                // The interceptor cannot name MultiTenancyOptions — that package references
                // Persistence.EFCore, so the reverse would be a cycle — and it defaults the guard to off
                // so an interceptor built by hand behaves as it always did. The host can name the
                // options, so the answer travels as a bool from here: without it the application's
                // RequireTenant would decide reads and nothing else.
                AppendLine(
                    "requireTenant: sp.GetRequiredService<global::Microsoft.Extensions.Options.IOptions<"
                    + "global::Pragmatic.MultiTenancy.MultiTenancyOptions>>().Value.RequireTenant),");
                DecreaseIndent();
            }

            // Roll-up (#2) sits BEFORE DomainEvents:
            // its owned transaction (relational aggregate increments) commits in SavedChanges before
            // the domain-event dispatch runs, so handlers in fresh scopes read committed data.
            // No-op when no [RollUp] rules are registered.
            AppendLine("new global::Pragmatic.Persistence.EFCore.RollUp.RollUpInterceptor(");
            IncreaseIndent();
            if (_hasEvents)
                AppendLine("sp.GetServices<global::Pragmatic.Persistence.RollUp.RollUpRule>()),");
            else
                AppendLine("sp.GetServices<global::Pragmatic.Persistence.RollUp.RollUpRule>()));");
            DecreaseIndent();

            if (_hasEvents)
            {
                // [Raises<TEvent>(on: ...)] emits RaiseLifecycleEvents on the entity; this interceptor
                // is the only caller. It raises during SavingChanges, so it must precede the outbox
                // capture interceptors registered further down — those read the entity's events in
                // SavingChanges, and anything raised after them never reaches the outbox.
                //
                // Raising only. What dispatches is EfCoreUnitOfWork, after the save, in the scope that
                // asked for it. An interceptor dispatching from a scope of its own would find no tenant
                // resolved, and every fail-closed filter would hide the rows a handler came for.
                // Last in the list, so it closes AddInterceptors( too.
                AppendLine("new global::Pragmatic.Events.EFCore.LifecycleEventsInterceptor());");
            }
            DecreaseIndent();

            if (boundary.HasAuditedEntities)
            {
                AppendLine();
                Comment("Audit-log interceptor — appends __AuditLog rows for [Audited] entities, same transaction");
                AppendLine("options.AddInterceptors(new global::Pragmatic.Persistence.EFCore.Auditing.AuditLogInterceptor(");
                AppendLine("    sp.GetService<global::System.TimeProvider>() ?? global::System.TimeProvider.System,");
                AppendLine("    sp.GetService<global::Pragmatic.Identity.ICurrentUser>()));");
            }

            if (boundary.HasEventOutbox)
            {
                AppendLine();
                Comment("Event outbox interceptor — captures domain events into __EventOutbox in the same transaction");
                AppendLine("options.AddInterceptors(sp.GetRequiredService<global::Pragmatic.Events.EFCore.Outbox.EventOutboxInterceptor>());");
            }

            if (boundary.HasMessagingOutbox)
            {
                AppendLine();
                Comment("Messaging outbox interceptor — captures domain events into __OutboxMessages in the same transaction");
                AppendLine("options.AddInterceptors(sp.GetRequiredService<global::Pragmatic.Messaging.Entities.OutboxInterceptor>());");
            }

            AppendLine();
            Comment("DI-registered interceptors (e.g. DB-per-tenant connection routing, MT-H2) — no-op when none registered");
            AppendLine("options.AddInterceptors(sp.GetServices<global::Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>());");

            AppendLine("});");
            AppendLine();

            // [EnableEventOutbox]: register the delivery background service, options and capture interceptor.
            if (boundary.HasEventOutbox)
            {
                Comment("Transactional event outbox: delivery loop + options + capture interceptor");
                AppendLine($"global::Pragmatic.Events.EFCore.Outbox.EventOutboxExtensions.AddEventOutbox<{boundary.ClassName}>(services);");
                AppendLine();
            }

            // [EnableOutbox]: register the per-boundary EF outbox source, capture interceptor, and
            // (deduped across boundaries) the transport-publish delivery pump + retention purge service.
            if (boundary.HasMessagingOutbox)
            {
                Comment("Transactional messaging outbox: per-boundary source + interceptor + delivery/purge");
                AppendLine($"global::Pragmatic.Messaging.EFCore.Outbox.MessagingOutboxExtensions.AddMessagingOutbox<{boundary.ClassName}>(services, \"{boundary.BoundaryName}\");");
                AppendLine();
            }

            // [EnableSagaPersistence]: register the marker (keyed by DbContext full name) carrying the concrete
            // DbContext type. The boundary-library saga registration resolves it lazily — it cannot reference
            // this host-generated DbContext type at compile time. Key must match StripGlobal(PersistenceDbContextFqn).
            if (boundary.HasSagaPersistence)
            {
                var dbContextFqn = $"global::{boundary.Namespace}.{boundary.ClassName}";
                var markerKey = $"{boundary.Namespace}.{boundary.ClassName}";
                Comment("Saga persistence marker — resolved lazily by the boundary-library EfCoreSagaRepository registration");
                AppendLine($"services.AddKeyedSingleton(\"{markerKey}\", new global::Pragmatic.Messaging.Configuration.SagaPersistenceMarker(typeof({dbContextFqn})));");
                AppendLine();
            }

            // [EnableBatchProgress]: register the single-owner EF batch-progress store bound to this
            // boundary's DbContext (host-side type available — no keyed marker needed, unlike saga).
            if (boundary.HasBatchProgress)
            {
                Comment("Batch progress store: single-owner EF store bound to this boundary's DbContext");
                AppendLine($"global::Pragmatic.Messaging.Batch.BatchProgressExtensions.AddBatchProgress<{boundary.ClassName}>(services);");
                AppendLine();
            }

            // [EnableJobPersistence]: the durable job store takes a bare DbContext — EfCoreJobStore and
            // EfCoreRecurringJobStore both do — and every other registration here is keyed by boundary,
            // so an unkeyed resolution found nothing and the store failed on first use. The boundary that
            // declared the tables is the one to forward to, which is what makes this unambiguous.
            if (boundary.HasJobPersistence)
            {
                Comment("Durable job store: the bare DbContext it resolves, forwarded to the boundary that holds __Jobs");
                AppendLine(
                    "services.AddScoped<global::Microsoft.EntityFrameworkCore.DbContext>("
                    + $"sp => sp.GetRequiredService<{boundary.ClassName}>());");
                AppendLine();
            }

            // Register keyed DbContext by boundary type (for DomainActionInvoker)
            if (!string.IsNullOrEmpty(boundary.BoundaryTypeName))
            {
                // Use global:: to avoid namespace resolution issues
                var qualifiedBoundaryType = boundary.BoundaryTypeName!.StartsWith("global::", StringComparison.Ordinal)
                    ? boundary.BoundaryTypeName
                    : $"global::{boundary.BoundaryTypeName}";
                Comment("Keyed registration for boundary type (used by DomainActionInvoker)");
                AppendLine(
                    $"services.AddKeyedScoped<global::Microsoft.EntityFrameworkCore.DbContext>(typeof({qualifiedBoundaryType}),");
                AppendLine($"    (sp, _) => sp.GetRequiredService<{boundary.ClassName}>());");
            }

            AppendLine();
            Comment("IUnitOfWork wrapping this boundary's DbContext (keyed by boundary for correct scoping)");
            if (!string.IsNullOrEmpty(boundary.BoundaryTypeName))
            {
                var qualifiedBoundaryType = boundary.BoundaryTypeName!.StartsWith("global::", StringComparison.Ordinal)
                    ? boundary.BoundaryTypeName
                    : $"global::{boundary.BoundaryTypeName}";
                AppendLine(
                    $"services.AddKeyedScoped<global::Pragmatic.Persistence.Repository.IUnitOfWork>(typeof({qualifiedBoundaryType}), (sp, _) =>");
                AppendLine(
                    $"    new global::Pragmatic.Persistence.EFCore.UnitOfWork.EfCoreUnitOfWork(");
                AppendLine(
                    $"        sp.GetRequiredService<{boundary.ClassName}>(),");
                AppendLine(
                    "        sp.GetService<global::Microsoft.Extensions.Logging.ILogger<"
                    + "global::Pragmatic.Persistence.EFCore.UnitOfWork.EfCoreUnitOfWork>>(),");
                // GetService, not GetRequiredService: an application with no domain events registers no
                // dispatcher, and demanding one would refuse to build a unit of work over a feature it
                // does not use. Without it a save with no invoker above it leaves the events on the
                // entity rather than dropping them.
                AppendLine(
                    "        sp.GetService<global::Pragmatic.Events.IDomainEventDispatcher>(),");
                // The [GeneratedValue] bindings, filled before the save rather than by an interceptor:
                // a {SEQ} format asks the database for its next value, which cannot be done from
                // inside the save it would be part of. Empty in an application that declares none.
                AppendLine(
                    "        sp.GetServices<global::Pragmatic.Persistence.Lifecycle."
                    + "IGeneratedValueBinding>(),");
                AppendLine("        sp.GetService<global::System.TimeProvider>(),");
                AppendLine(
                    "        sp.GetService<global::Pragmatic.Identity.ICurrentUser>()));");
            }

            AppendLine();
            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Public, new MethodModifiers { IsStatic = true });
    }

    private void RenderAddAllDbContextsMethod()
    {
        XmlSummary("Registers all boundary DbContexts with keyed DI.");
        XmlParam("services", "The service collection.");
        XmlParam("configure", "Configuration action for the DbContext options (shared by all).");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services"),
            new("Action<DbContextOptionsBuilder>", "configure")
        };

        Method("AddAllPragmaticDbContexts", () =>
        {
            AppendLine();

            foreach (var boundary in _boundaries.Where(b => !b.IsMigrationContext))
                AppendLine($"services.Add{boundary.BoundaryName}DbContext(configure);");
            AppendLine();

            Comment("Auto-register EfCoreQueryExecutor with full filter pipeline (soft-delete, tenant, navigation filters, caching)");
            AppendLine("services.AddScoped<global::Pragmatic.Persistence.Query.Executors.IQueryExecutor>(sp =>");
            IncreaseIndent();
            AppendLine("new global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider>(),");
            AppendLine("sp.GetService<global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer>(),");
            AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle>(),");
            AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStack>(),");
            AppendLine("sp.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor>>(),");
            // Include tenant + user discriminators in the query cache key so cached
            // rows are never shared across tenants or users (row-level authorization leak otherwise).
            AppendLine("sp.GetService<global::Pragmatic.MultiTenancy.ITenantContext>(),");
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>(),");
            // The resolver routes a [Cacheable(Category = ...)] query to that category's stack.
            // Without it the executor falls back to the default one and a category's key prefix
            // and duration apply to nothing.
            AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStackResolver>(),");
            // The clock the FilterContext's Now comes from. Without it the executor falls back
            // to the wall clock and a test that pins time cannot pin a temporal filter.
            AppendLine("sp.GetService<global::System.TimeProvider>(),");
            // Where a [Join<T>(ForeignKey = …)] gets its target set: the root boundary's own
            // DbContext, through the keyed registration above. EF Core composes a join only within
            // one context instance, and a host builds one per boundary.
            AppendLine("new global::Pragmatic.Persistence.EFCore.Query.BoundaryJoinSources(sp)));");
            DecreaseIndent();
            DecreaseIndent();
            AppendLine();
            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Public, new MethodModifiers { IsStatic = true });
    }
}

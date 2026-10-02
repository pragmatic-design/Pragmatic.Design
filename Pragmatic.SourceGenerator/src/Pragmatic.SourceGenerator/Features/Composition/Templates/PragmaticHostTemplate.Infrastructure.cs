// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Infrastructure)

using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Partial: Infrastructure registrations — repositories, pipeline steps, infra modules, databases.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderRegisterAllRepositoriesMethod()
    {
        XmlSummary("Registers all repositories from discovered modules.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services")
        };

        Method("RegisterAllRepositories", () =>
        {
            if (!HasDiscoveredRepositories)
            {
                Comment("No repositories discovered");
                AppendLine("return services;");
                return;
            }

            var grouped = _model.DiscoveredRepositories
                .GroupBy(r => r.BoundaryName ?? "Other")
                .OrderBy(g => g.Key);

            foreach (var group in grouped)
            {
                Comment(group.Key);
                foreach (var repo in group.OrderBy(r => r.EntityType))
                {
                    var repoType = $"global::{repo.RepositoryType}";
                    var entityType = $"global::{repo.EntityType}";
                    var idType = GetFullIdTypeForRepo(repo.IdType);

                    AppendLine($"services.AddScoped<{repoType}>();");
                    AppendLine(
                        $"services.AddScoped<global::Pragmatic.Persistence.Repository.IRepository<{entityType}>>(sp => sp.GetRequiredService<{repoType}>());");
                    AppendLine(
                        $"services.AddScoped<global::Pragmatic.Persistence.Repository.IReadRepository<{entityType}>>(sp => sp.GetRequiredService<{repoType}>());");
                }
            }

            RenderQueryFilterRegistrations();
            RenderLookupCacheRegistrations();

            AppendLine();
            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Calls each referenced boundary's generated query-filter registration.
    /// </summary>
    /// <remarks>
    ///     Without this the filters for <c>[HasAccessScopes]</c>, <c>[HasOwner]</c>, tenant and
    ///     soft-delete are generated and never registered: the application starts, every request
    ///     answers 200, and row-level access control is simply absent. A consumer found it by
    ///     noticing that a site manager could see every site. The Showcase never did, because
    ///     <c>ShowcaseStartupStep</c> calls the method by hand.
    /// </remarks>
    private void RenderQueryFilterRegistrations()
    {
        // ⚠️ Not from a module this host only reaches over HTTP. These entries carry the value
        // generators as well as the filters, and a sequence-backed generator takes the boundary's
        // DbContext — which is registered from an [Include<TModule, TDatabase>] that a remote module
        // by definition does not have. Registered anyway, they were the first thing the distributed
        // host's container validation named once it had something to start.
        var registrations = HostedRegistrationMethodsFor(MetadataCategoryIds.Persistence);

        if (registrations.Count == 0)
            return;

        AppendLine();
        Comment("Query filters (soft delete, tenant, ownership, scope) — generated per boundary");
        foreach (var registration in registrations)
            AppendLine($"global::{registration}(services);");
    }

    /// <summary>
    ///     Calls each referenced boundary's generated lookup-cache registration.
    /// </summary>
    /// <remarks>
    ///     The same defect as the query filters, one feature over: <c>Add{Prefix}LookupCaches()</c> is
    ///     generated in the assembly that declares a <c>[Lookup]</c> and the host never called it, so
    ///     there was no <c>ILookupCache&lt;T, TId&gt;</c> in the container, no <c>ILookupCacheLoader</c>
    ///     and no preload. A nullable lookup navigation then returned <c>null</c> on every read and a
    ///     non-nullable one threw — and the documentation had turned the gap into an instruction, telling
    ///     the reader to call the method by hand "even inside a host". The Showcase did exactly that,
    ///     which is why no test here was red.
    /// </remarks>
    private void RenderLookupCacheRegistrations()
    {
        if (_model.DiscoveredLookupRegistrations.IsDefaultOrEmpty)
            return;

        AppendLine();
        Comment("Lookup caches and their preload — generated per assembly that declares a [Lookup]");
        foreach (var registration in _model.DiscoveredLookupRegistrations)
            AppendLine($"global::{registration}(services);");
    }

    private static string GetFullIdTypeForRepo(string idType)
    {
        return idType switch
        {
            "int" => "int",
            "long" => "long",
            "string" => "string",
            _ when idType.Contains(".") => $"global::{idType}",
            _ => idType
        };
    }

    private void RenderRegisterAllPipelineStepsMethod()
    {
        XmlSummary("Registers all IStartupStep implementations.");
        XmlParam("services", "The service collection.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services")
        };

        Method("RegisterAllPipelineSteps", () =>
        {
            if (!HasPipelineSteps)
            {
                Comment("No startup steps discovered");
                AppendLine("return services;");
                return;
            }

            // Steps from [NeedsStep<T>] declarations on [Module] classes
            if (HasNeedsSteps)
            {
                Comment("Steps from [NeedsStep<T>] (deduplicated across modules)");
                foreach (var stepFqn in _model.AggregatedNeedsSteps.OrderBy(s => s))
                    AppendLine($"services.AddSingleton<IStartupStep, {stepFqn}>();");

                AppendLine();
            }

            // Local [StartupStep] classes (excluding types already registered via NeedsStep)
            if (_localStartups.Length > 0)
            {
                var needsStepSet = HasNeedsSteps
                    ? new HashSet<string>(_model.AggregatedNeedsSteps)
                    : new HashSet<string>();

                var localOnly = _localStartups
                    .Where(s => !needsStepSet.Contains(s.FullTypeName))
                    .OrderBy(s => s.Priority)
                    .ThenBy(s => s.FullTypeName)
                    .ToList();

                if (localOnly.Count > 0)
                {
                    Comment("Local [StartupStep] classes");
                    foreach (var startup in localOnly)
                        AppendLine($"services.AddSingleton<IStartupStep, {startup.FullTypeName}>();");

                    AppendLine();
                }
            }

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    private void RenderInfraModuleRegistrations()
    {
        var f = _model.DetectedFeatures;
        Comment("Infrastructure modules (auto-detected from referenced assemblies)");

        if (f.HasEndpoints)
        {
            Comment("Security response headers (Order 5 — before everything, so error responses carry them too)");
            AppendLine(
                "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.SecurityHeadersStep>();");
            Comment("Request body size limits ([MaxBodySize] metadata + Pragmatic:RequestLimits config; no-op when unused)");
            AppendLine(
                "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.RequestLimitsStep>();");
            // The middleware throws at startup without AddRateLimiter, which the endpoint services emit
            // only when some module exposes an endpoint — the Endpoints package alone is not that.
            // Same condition, so the two cannot disagree.
            if (HasEndpoints)
            {
                Comment("Rate limiting (Order 92 — after authentication). Without the middleware every");
                Comment("[RateLimit] is metadata nobody reads, and the endpoint permits every request.");
                AppendLine(
                    "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.RateLimiterStep>();");
            }
            if (HasOutputCacheMetadata)
            {
                Comment("Output cache (Order 95 — after authorization). A shared [ResponseCache] puts CacheOutput on");
                Comment("the route, which keeps nothing without these two.");
                AppendLine("services.AddOutputCache();");
                AppendLine(
                    "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.OutputCacheStep>();");
            }
            Comment("Antiforgery for [RequireAntiforgery] endpoints (middleware is pass-through elsewhere)");
            AppendLine("services.AddAntiforgery();");
            AppendLine(
                "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Steps.AntiforgeryStep>();");
        }

        if (f.HasTemporalAspNetCore)
        {
            AddUsing("Pragmatic.Temporal.AspNetCore");
            Comment("Temporal web integration: core services + timezone detection + JSON behaviors (MVC and Minimal API)");
            AppendLine("services.AddPragmaticTemporalAspNetCore();");
            Comment("Timezone detection middleware (Order 100 — after typical authentication steps)");
            AppendLine(
                "services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Temporal.AspNetCore.Steps.TemporalContextStep>();");
        }
        else if (f.HasTemporal)
        {
            AddUsing("Pragmatic.Temporal.Extensions");
            AppendLine("services.AddPragmaticTemporal();");
        }

        // ⚠️ Presence AND declaration. The package is on the compilation whether the application
        // asked for it or somebody else's dependency brought it, so a host that uses resilience in no
        // file would get the registration on presence alone. The flag says the extension method can be named; the
        // metadata says somebody declared a policy.
        if (WiresResilience)
        {
            AddUsing("Pragmatic.Resilience");
            AddUsing("Pragmatic.Resilience.Configuration");
            AppendLine("services.AddPragmaticResilience();");
            AppendLine("services.Configure<ResilienceOptions>(configuration.GetSection(\"Resilience\"));");
        }

        if (f.HasCaching && HasCachingMetadata)
        {
            AddUsing("Pragmatic.Caching.Extensions");
            AppendLine("services.AddHybridCache();");

            // If categories are discovered, register with CachingBuilder; otherwise plain registration
            if (!_model.DiscoveredCacheCategories.IsDefaultOrEmpty)
            {
                AppendLine("services.AddPragmaticCaching(cache =>");
                AppendLine("{");
                IncreaseIndent();
                Comment("Auto-detected cache categories from [Cacheable(Category=...)] / [InvalidatesCache(Category=...)]");
                foreach (var category in _model.DiscoveredCacheCategories)
                    AppendLine($"cache.ForCategory<{category}>(_ => {{ }});");
                DecreaseIndent();
                AppendLine("});");
            }
            else
            {
                AppendLine("services.AddPragmaticCaching();");
            }
        }

        // Gated on the ASP.NET Core package, not on HasI18n: the base package can be referenced
        // transitively while this one is not, and the using below would then name an assembly the
        // compilation cannot resolve — a generated file that does not compile.
        if (f.HasI18nAspNetCore && HasTranslationsMetadata)
        {
            AddUsing("Pragmatic.Internationalization.AspNetCore.Extensions");
            AppendLine("services.AddPragmaticInternationalization(configuration);");

            // The translations the modules compiled in, as sources a lookup by key reaches.
            foreach (var provider in _model.TranslationProviders)
                AppendLine($"services.AddPragmaticLocalizationProvider<{provider}>();");
            RenderDeclaredLanguages();
        }

        // The template sources the included modules declare. Each module's startup step would be the
        // natural place, and it is not discovered across assemblies: without this every host wrote the
        // line by hand for every module it included.
        if (f.HasPdxTemplates && _model.PdxTemplateAnchors.Count > 0)
        {
            AppendLine("global::Pragmatic.Documents.Markup.PdxTemplatesServiceCollectionExtensions.AddPdxTemplates(services, templates => templates");
            IncreaseIndent();
            for (var i = 0; i < _model.PdxTemplateAnchors.Count; i++)
            {
                var last = i == _model.PdxTemplateAnchors.Count - 1;
                AppendLine($".FromAssemblyOf<{_model.PdxTemplateAnchors[i]}>(){(last ? ");" : "")}");
            }
            DecreaseIndent();
        }
        else if (f.HasI18nAspNetCore && HasI18nWireTypeMetadata)
        {
            // An amount on the wire and nothing translated: the converters are all such an application
            // asked for, and without them every request carrying one is a 400 before any rule runs —
            // while the application starts, because with no i18n registered it never reaches the
            // configuration check that would have refused. Not the full registration: a
            // culture provider, resources and a request culture nobody declared would be presence
            // wiring, which is what the rule above exists to refuse.
            AddUsing("Pragmatic.Internationalization.AspNetCore.Json.Extensions");
            AppendLine(
                "services.ConfigureHttpJsonOptions(json => "
                + "json.SerializerOptions.AddPragmaticInternationalization());");
        }

        if (f.HasIdentityAspNetCore)
        {
            AddUsing("Pragmatic.Identity");
            AppendLine("services.AddPragmaticIdentity();");
        }

        if (f.HasMultiTenancy)
        {
            AddUsing("Pragmatic.MultiTenancy");
            AppendLine("services.AddPragmaticMultiTenancy();");
        }

        if (f.HasFeatureFlags && HasFeatureFlagMetadata)
        {
            AddUsing("Pragmatic.FeatureFlags");
            AppendLine("services.AddPragmaticFeatureFlags();");
        }

        if (f.HasDiscovery)
        {
            AddUsing("Pragmatic.Discovery.Extensions");
            AppendLine("services.AddDiscovery();");
        }

        // Presence AND declaration, as for resilience: the package is on the compilation whether the
        // application asked for it or a dependency brought it, and MessageHandlers is the category a
        // module publishes when somebody wrote [MessageHandler].
        if (f.HasMessaging && HasMessageHandlerMetadata)
        {
            AddUsing("Pragmatic.Messaging.Extensions");
            AppendLine("services.AddPragmaticMessaging();");
        }

        if (f.HasJobs && HasJobMetadata)
        {
            AddUsing("Pragmatic.Jobs.Extensions");
            AppendLine("services.AddPragmaticJobs();");
            AppendLine("services.AddJobProcessingServices();");
        }

        if (f.HasNotifications)
        {
            AddUsing("Pragmatic.Notifications.Extensions");
            AppendLine("services.AddPragmaticNotifications();");
        }

        AppendLine();
    }

    private void RenderControlPlaneRegistrations()
    {
        Comment("Control plane host identity and status (NoOp default, replaced by UseAgent)");
        // ⚠️ The name is passed, not looked up. Read from AssemblyMetadataRegistry with FindByCategory —
        // the FIRST provider carrying a topology, while the generator emits one per host from a
        // [ModuleInitializer] — two hosts in one process would both report the name of whichever
        // assembly loaded first. It is the same expression the topology JSON uses, so the two
        // cannot disagree, and the registry stays as the fallback for a host that passes nothing.
        AppendLine(
            "services.AddSingleton<global::Pragmatic.ControlPlane.IHostIdentity>("
            + "new global::Pragmatic.Composition.ControlPlane.LocalHostIdentity("
            + $"hostName: \"{HostName.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"));");
        AppendLine("services.AddSingleton<global::Pragmatic.Composition.ControlPlane.LocalHostStatus>();");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostStatus>(sp => sp.GetRequiredService<global::Pragmatic.Composition.ControlPlane.LocalHostStatus>());");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IControlPlane>(sp => new global::Pragmatic.Composition.ControlPlane.NoOpControlPlane(");
        IncreaseIndent();
        AppendLine("sp.GetRequiredService<global::Pragmatic.ControlPlane.IHostIdentity>(),");
        AppendLine("sp.GetRequiredService<global::Pragmatic.ControlPlane.IHostStatus>()));");
        DecreaseIndent();
        Comment("Cluster leadership — NoOp default (always-leader monolith), replaced by UseAgent's KV-lease election");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IClusterLeadership, global::Pragmatic.Composition.ControlPlane.NoOpClusterLeadership>();");
        Comment("Maintenance mode — default singleton (replaced by UseMaintenanceMode if called).");
        Comment("Factory (not a bare instance) so the logger is injected and any DI-registered");
        Comment("IMaintenanceModeObserver is attached — a bare instance would leave the observers unreachable.");
        AppendLine("global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<global::Pragmatic.Maintenance.IMaintenanceMode>(services, static sp =>");
        Block(() =>
        {
            AppendLine("var _maintenance = new global::Pragmatic.Composition.Hosting.MaintenanceModeService(");
            AppendLine("    global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Composition.Hosting.MaintenanceModeService>>(sp));");
            AppendLine("foreach (var _observer in global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<global::Pragmatic.Maintenance.IMaintenanceModeObserver>(sp))");
            AppendLine("    _maintenance.AddObserver(_observer);");
            AppendLine("return _maintenance;");
        });
        AppendLine(");");
        // The command handlers below flip IMaintenanceMode from the control plane. The flag only
        // becomes a 503 if something reads it on the request path, and this step is that reader:
        // without it an operator draining a host would get an active flag and full traffic.
        // Wired unconditionally; the admin endpoints it
        // can also map stay behind MaintenanceModeOptions.EnableAdminEndpoints.
        Comment("Maintenance 503 middleware (Order -100) — always wired, or an active maintenance flag lets traffic through");
        AppendLine("services.AddSingleton<global::Pragmatic.Composition.Abstractions.IStartupStep, global::Pragmatic.Composition.Hosting.MaintenanceStep>();");
        AppendLine("services.AddHostedService<global::Pragmatic.Composition.ControlPlane.HostStatusSyncService>();");
        Comment("Health aggregator — collects health reports from all IHostHealthContributor instances");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostHealthAggregator, global::Pragmatic.Composition.ControlPlane.HostHealthAggregator>();");
        Comment("Command dispatcher + built-in handlers (maintenance, drain)");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostCommandDispatcher, global::Pragmatic.Composition.ControlPlane.HostCommandDispatcher>();");
        Comment("Shared handle holder — thread-safe singleton owned by both maintenance command handlers");
        AppendLine("services.AddSingleton<global::Pragmatic.Composition.ControlPlane.MaintenanceHandleHolder>();");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostCommandHandler<global::Pragmatic.ControlPlane.EnterMaintenanceCommand>, global::Pragmatic.Composition.ControlPlane.MaintenanceCommandHandler>();");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostCommandHandler<global::Pragmatic.ControlPlane.ExitMaintenanceCommand>, global::Pragmatic.Composition.ControlPlane.ExitMaintenanceCommandHandler>();");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostCommandHandler<global::Pragmatic.ControlPlane.DrainCommand>, global::Pragmatic.Composition.ControlPlane.DrainCommandHandler>();");

        var f = _model.DetectedFeatures;

        Comment("MigrateCommand handler — no-op-with-log unless a host migration coordinator is present");
        AppendLine("services.AddSingleton<global::Pragmatic.ControlPlane.IHostCommandHandler<global::Pragmatic.ControlPlane.MigrateCommand>, global::Pragmatic.Composition.ControlPlane.MigrateCommandHandler>();");
        // Guarded on the same condition GenerateMigrationCoordinator emits on — a database with an
        // entity behind it — so registering the type never names a class that was not produced.
        if (f.HasMigrations && _model.HostIncludes.Any(i => i.MigrationDbContextFqn is not null))
        {
            Comment("On-demand migration coordinator (SG-generated) — makes MigrateCommand actually migrate");
            AppendLine($"services.AddSingleton<global::Pragmatic.ControlPlane.IHostMigrationCoordinator>(sp => new global::{_model.RootNamespace}.PragmaticMigrationCoordinator(sp, global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<global::Microsoft.Extensions.Configuration.IConfiguration>(sp)));");
        }

        if (f.HasMultiTenancy)
        {
            // ⚠️ Wrapped, not bare. Unwrapped, the store's four write methods tell nobody, so anything
            // derived from the set of tenants goes stale in silence — the [Lookup] preload first. The
            // observers come from the container, so the store has to be built from it: a bare
            // instance would leave them unreachable.
            Comment("Tenant store — wrapped so ITenantLifecycleObserver instances hear create/update/deactivate/delete");
            AppendLine("services.AddSingleton<global::Pragmatic.MultiTenancy.ITenantStore>(sp =>");
            AppendLine("    new global::Pragmatic.MultiTenancy.ObservedTenantStore(");
            AppendLine("        new global::Pragmatic.MultiTenancy.InMemoryTenantStore(),");
            AppendLine("        sp.GetServices<global::Pragmatic.MultiTenancy.ITenantLifecycleObserver>(),");
            AppendLine("        sp.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.MultiTenancy.ObservedTenantStore>>()));");
        }

        AppendLine();
    }

    private void RenderRegisterAllDatabasesMethod()
    {
        AddUsing("Microsoft.EntityFrameworkCore");
        AddUsing("Microsoft.Extensions.Configuration");
        AddUsing("Microsoft.Extensions.DependencyInjection");

        XmlSummary("Registers DbContext instances for all boundaries declared via [Include&lt;TModule, TDb&gt;].");
        XmlParam("services", "The service collection.");
        XmlParam("configuration", "The host configuration (connection strings).");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services"),
            new("IConfiguration", "configuration")
        };

        Method("RegisterAllDatabases", () =>
        {
            var includes = _model.HostIncludes.Where(i => i.HasDatabase).ToList();

            if (includes.Count == 0)
            {
                Comment("No database wiring configured");
                AppendLine("return services;");
                return;
            }

            // Build a lookup: module simple name -> DiscoveredModuleInfo (for 2-arity resolution).
            // First-wins dedup (NOT ToDictionary, which THREW on a duplicate Name and suppressed the
            // whole generated host, TOPO-DUPNAME-CRASH). Duplicates are reported as PRAG1607 by the validator.
            var moduleByName = new global::System.Collections.Generic.Dictionary<string, global::Pragmatic.SourceGenerator.Features.Composition.Models.DiscoveredModuleInfo>(global::System.StringComparer.Ordinal);
            foreach (var m in _model.DomainModules)
                if (m.DbContextRegistrationMethod is not null && !moduleByName.ContainsKey(m.Name))
                    moduleByName[m.Name] = m;

            // Group includes by database to share InMemoryDatabaseRoot per unique database
            var byDatabase = includes
                .GroupBy(i => i.DatabaseTypeName ?? string.Empty)
                .ToList();

            foreach (var dbGroup in byDatabase)
            {
                var representative = dbGroup.First();
                var dbSimpleName = GetSimpleName(dbGroup.Key);
                var provider = representative.DatabaseProvider;
                var configKey = representative.DatabaseConfigKey;

                Comment($"Database: {dbSimpleName} (provider: {provider ?? "default"})");

                // For InMemory: generate a shared root so all boundaries in the same DB share state
                string? inMemoryRootVar = null;
                if (provider == "InMemory")
                {
                    AddUsing("Microsoft.EntityFrameworkCore.Storage");
                    inMemoryRootVar = ToCamelCase(dbSimpleName) + "Root";
                    AppendLine($"var {inMemoryRootVar} = new InMemoryDatabaseRoot();");
                }

                foreach (var include in dbGroup)
                {
                    var moduleSimpleName = GetModuleSimpleName(include.ModuleTypeName);
                    var useMethod = DatabaseProviderCall.UseMethod(provider);
                    var argLine = BuildProviderArgLine(provider, configKey, inMemoryRootVar, dbSimpleName);

                    if (include.HasExplicitDbContext)
                    {
                        // 3-arity: explicit DbContext type provided. 2-arg AddDbContext so DI-registered
                        // interceptors (e.g. DB-per-tenant connection routing, MT-H2) are applied.
                        Comment($"Module: {moduleSimpleName} (explicit DbContext)");
                        AppendLine($"services.AddDbContext<{include.DbContextTypeName}>((sp, options) =>");
                        AppendLine("{");
                        AppendLine($"    options.{useMethod}({argLine});");
                        // An instance method of DbContextOptionsBuilder: EF Core has no static class to
                        // call it through, and the form that named one never compiled.
                        AppendLine("    options.AddInterceptors(global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<global::Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor>(sp));");
                        AppendLine("});");
                    }
                    else if (BoundariesOf(include, moduleByName, moduleSimpleName) is { Count: > 0 } boundaries)
                    {
                        // 2-arity: one call per boundary the included module declares, and the calls
                        // are the generated Add{Boundary}DbContext.
                        // ⚠️ Not ONE module looked up by name registering ONE context: a module with
                        // two boundaries matches no name at all, and its contexts, its keyed
                        // IUnitOfWork and its endpoints would be generated and never wired.
                        foreach (var boundary in boundaries)
                        {
                            if (_model.PersistedStores.HasDbContextRegistration(boundary.DbContextRegistrationMethod))
                            {
                                // Called through its class by full name: as an extension it resolved only
                                // when the host's assembly is named under the modules' root, whose
                                // namespace the class is declared in.
                                Comment($"Module: {moduleSimpleName} — boundary {boundary.Name}");
                                AppendLine($"{_model.PersistedStores.RegistrationClass}.{boundary.DbContextRegistrationMethod}(services, options =>");
                                AppendLine($"    options.{useMethod}({argLine}));");
                            }
                            else
                            {
                                // The registration is written per boundary with entities; a boundary
                                // without any yet — every application's first state — has no DbContext.
                                Comment($"Module: {moduleSimpleName} — boundary {boundary.Name} (no entities, no DbContext)");
                            }
                        }
                    }
                    else
                    {
                        // Module metadata not found -- emit a comment so the developer is informed
                        Comment($"WARN: Module '{moduleSimpleName}' not found in discovered modules — ensure Persistence.EFCore.SG runs in {include.ModuleTypeName}");
                    }
                }

                RenderTrailContexts(dbGroup.Key, provider, configKey, inMemoryRootVar, dbSimpleName);
                AppendLine();
            }

            RenderAuditTrailAmbiguity();

            // The fluent QueryBuilder's AsNoTracking()/AsSplitQuery() are EF Core concepts, and the
            // assembly that declares them does not reference EF Core. Installed here because this is
            // the path every EF host takes: AddPragmaticPersistenceEFCore exists and nobody calls it.
            Comment("Let the fluent builder's hints reach EF Core");
            AppendLine("global::Pragmatic.Persistence.EFCore.Query.EfQueryHintApplier.Install();");
            AppendLine();

            // Auto-register EfCoreQueryExecutor with full filter pipeline
            Comment("Auto-register EfCoreQueryExecutor (soft-delete, tenant, navigation filters, caching)");
            AppendLine("services.AddScoped<global::Pragmatic.Persistence.Query.Executors.IQueryExecutor>(sp =>");
            IncreaseIndent();
            AppendLine("new global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor(");
            IncreaseIndent();
            AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterProvider>(),");
            AppendLine("sp.GetService<global::Pragmatic.Persistence.EFCore.Query.FilterMapComposer>(),");
            AppendLine("sp.GetService<global::Pragmatic.Persistence.Query.Filters.IQueryFilterToggle>(),");
            AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStack>(),");
            AppendLine("sp.GetService<global::Microsoft.Extensions.Logging.ILogger<global::Pragmatic.Persistence.EFCore.Query.EfCoreQueryExecutor>>(),");
            AppendLine("sp.GetService<global::Pragmatic.MultiTenancy.ITenantContext>(),");
            // Pass ICurrentUser so the query cache key includes the user discriminator
            // (permission/ownership filters) — without it cached rows leak across users.
            AppendLine("sp.GetService<global::Pragmatic.Identity.ICurrentUser>(),");
            // The resolver routes a [Cacheable(Category = ...)] query to that category's stack.
            // Without it the executor falls back to the default one and a category's key prefix
            // and duration apply to nothing.
            AppendLine("sp.GetService<global::Pragmatic.Caching.ICacheStackResolver>(),");
            // The clock the FilterContext's Now comes from. Without it the executor falls back
            // to the wall clock and a test that pins time cannot pin a temporal filter.
            AppendLine("sp.GetService<global::System.TimeProvider>(),");
            // Where a [Join<T>(ForeignKey = …)] gets its target set: the root boundary's own
            // DbContext, through the keyed registration the boundary contexts already have.
            // ⚠️ This construction exists TWICE — here and in DbContextRegistrationTemplate — and a
            // host composed by PragmaticApp uses this one. Changing only the other copy leaves every
            // joined query answering 500 with the executor's own refusal: "built without an
            // IJoinSourceProvider".
            AppendLine("new global::Pragmatic.Persistence.EFCore.Query.BoundaryJoinSources(sp)));");
            DecreaseIndent();
            DecreaseIndent();
            AppendLine();

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     Every boundary the included module brings, in declaration order.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Matched on the <b>assembly</b> that declares the module, because that is what an
    ///         <c>[Include&lt;TModule, …&gt;]</c> actually names. The discovery already yields one
    ///         <c>DiscoveredModuleInfo</c> per <c>[Boundary]</c>, so an assembly with two boundaries
    ///         comes back as two.
    ///     </para>
    ///     <para>
    ///         ⚠️ The name lookup stays as the fallback, for an include whose module symbol did not
    ///         resolve: there the assembly is unknown, and the name reading — module simple name
    ///         against a name derived from the boundary — is the only one available. With one boundary
    ///         named after its module the two readings agree, so a mismatch between them does not show
    ///         on the common shape.
    ///     </para>
    /// </remarks>
    private List<Models.DiscoveredModuleInfo> BoundariesOf(
        Models.HostIncludeModel include,
        Dictionary<string, Models.DiscoveredModuleInfo> moduleByName,
        string moduleSimpleName)
    {
        if (include.ModuleAssemblyName is { Length: > 0 } assembly)
        {
            var ofAssembly = _model.DomainModules
                .Where(m => string.Equals(m.AssemblyName, assembly, StringComparison.Ordinal))
                .ToList();

            if (ofAssembly.Count > 0)
                return ofAssembly;
        }

        return moduleByName.TryGetValue(moduleSimpleName, out var byName)
            ? [byName]
            : [];
    }

    private static string BuildProviderArgLine(
        string? provider,
        string? configKey,
        string? inMemoryRootVar,
        string dbSimpleName) =>
        provider switch
        {
            "InMemory" => inMemoryRootVar is not null
                ? $"\"{dbSimpleName}\", {inMemoryRootVar}"
                : $"\"{dbSimpleName}\"",
            _ => string.IsNullOrEmpty(configKey)
                ? DatabaseProviderCall.Arguments(provider, null, null)
                : DatabaseProviderCall.Arguments(provider, $"configuration[\"{configKey}\"]", configKey)
        };
}

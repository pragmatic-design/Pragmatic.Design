// Pragmatic.SourceGenerator - Composition - Pragmatic Entry Point Template

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Generates Pragmatic.g.cs with RunAsync(), RunWorkerAsync().
/// </summary>
internal sealed class PragmaticEntryTemplate : CSharpTemplate
{
    private readonly HostAggregationModel _model;

    // Expressions the database-init blocks resolve against. Web host = "app"; the generic worker host
    // has no WebApplication, so it points them at "host"/"builder".
    private string _appServicesExpr = "app.Services";
    private string _appConfigExpr = "app.Configuration";

    public PragmaticEntryTemplate(HostAggregationModel model)
    {
        _model = model;
    }

    protected override string? GeneratorName => "Pragmatic.SourceGenerator/Composition";

    public override Artifact RenderOutput()
    {
        return new Artifact("Host.Entry.g.cs", ToSourceText());
    }

    public override void RenderFile()
    {
        AddUsing("Microsoft.AspNetCore.Builder");
        AddUsing("Microsoft.Extensions.Configuration");
        AddUsing("Microsoft.Extensions.DependencyInjection");
        AddUsing("Microsoft.Extensions.Hosting");
        AddUsing("Pragmatic.Composition");
        AddUsing("Pragmatic.Composition.Hosting");

        AppendNamespace("Pragmatic.Composition.Hosting");
        AppendLine();

        Class("PragmaticApp", RenderClassBody,
            accessModifier: AccessModifier.Public,
            modifiers: new ClassModifiers { IsStatic = true });
    }

    private void RenderClassBody()
    {
        // RunAsync
        XmlSummary("Runs a web application with auto-discovered Pragmatic modules.");

        var runParams = new List<MethodParameter>
        {
            new("string[]", "args"),
            new() { Type = "Action<IPragmaticBuilder>?", Name = "configure", DefaultValue = "null" }
        };

        Method("RunAsync", RenderRunAsyncBody, "Task", runParams,
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true, IsAsync = true });

        AppendLine();

        // RunWorkerAsync
        XmlSummary("Runs a background worker host with auto-discovered Pragmatic modules.");

        Method("RunWorkerAsync", RenderRunWorkerAsyncBody, "Task", runParams,
            AccessModifier.Public,
            new MethodModifiers { IsStatic = true, IsAsync = true });
    }

    private void RenderRunAsyncBody()
    {
        AppendLine("var builder = WebApplication.CreateBuilder(args);");
        AppendLine();

        if (_model.HasRequiredConfigValidation)
        {
            Comment("Validate required configuration sections (fail-fast before DI registration)");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.ValidateConfiguration(builder.Configuration);");
            AppendLine();
        }

        Comment("SG defaults — auto-register infrastructure modules based on referenced assemblies");
        AppendLine($"global::{_model.RootNamespace}.PragmaticHost.RegisterAllPragmaticServices(builder.Services, builder.Configuration);");
        AppendLine();

        Comment("Module strategy configuration — user overrides defaults via IPragmaticBuilder");
        AppendLine("var pragmaticBuilder = new PragmaticBuilder(builder.Services, builder.Configuration, builder.Environment);");
        AppendLine("configure?.Invoke(pragmaticBuilder);");
        AppendLine();

        RenderMaintenanceOptionsRegistration();
        RenderHostHealthRegistration();
        RenderTelemetrySetup("builder.Services", "pragmaticBuilder.Options.Telemetry", "builder.Environment.IsDevelopment()", "builder.Configuration");
        RenderJsonDefaults();

        if (_model.HasDatabaseRegistrations)
        {
            AppendLine("// Register DbContext instances for configured databases");
            AppendLine(
                $"global::{_model.RootNamespace}.PragmaticHost.RegisterAllDatabases(builder.Services, builder.Configuration);");
            AppendLine();
        }

        AppendLine("// Call ConfigureServices on all pipeline steps (business wiring)");
        AppendLine(
            $"global::{_model.RootNamespace}.PragmaticHost.CallConfigureServices(builder.Services, builder.Configuration, builder.Environment);");
        AppendLine();
        AppendLine("// Register [ServiceFactory] classes and their [Factory] methods");
        AppendLine(
            $"global::{_model.RootNamespace}.PragmaticServiceFactories.AddPragmaticServiceFactories(builder.Services);");

        // Last, deliberately: a [Decorator] has to be able to wrap a service that UseAuthorization or
        // an IStartupStep registered, and Decorate throws when there is nothing there yet.
        if (_model.HasDecorators)
        {
            AppendLine();
            AppendLine("// Apply [Decorator]s — after every other registration");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.ApplyDecorators(builder.Services);");
        }

        // ⚠️ Nothing disables container validation here, not even for a partial topology. A partial
        // topology does not register workers of modules it does not host: a module the host does not
        // [Include] has its whole metadata filtered out by assembly
        // (HostModeGenerator.BuildIncludedAssemblyNames), and the one leak in that filter — a
        // [RemoteBoundary<T>] assembly, which the set adds on purpose for the HTTP invokers — contributes
        // no workers.
        //
        // ValidateOnBuild is the only thing that walks every registered descriptor and reports, at
        // startup, one that cannot be constructed. Off for the process, it would hide every
        // unresolvable registration in the host and not merely the one it was turned off for. Leaving
        // it alone keeps ASP.NET Core's own default: on in Development, which is where a developer is.

        AppendLine();
        AppendLine("var app = builder.Build();");
        AppendLine();
        Comment("Map infrastructure exceptions to problem responses (optimistic-concurrency conflict → 409)");
        Comment("as the outermost middleware, before any business pipeline or endpoint runs.");
        AppendLine("app.UsePragmaticExceptionMapping();");
        AppendLine();
        Comment("Maintenance mode opt-out: tests set Pragmatic:MaintenanceMode:EnableOnStartupFailure=false via config");
        AppendLine("var maintenanceEnabled = pragmaticBuilder.Options.MaintenanceMode.EnableOnStartupFailure");
        AppendLine("    && !string.Equals(builder.Configuration[\"Pragmatic:MaintenanceMode:EnableOnStartupFailure\"], \"false\", StringComparison.OrdinalIgnoreCase);");
        AppendLine();
        AppendLine("try");
        Block(() =>
        {
            // Database initialization (migrations / EnsureCreated) runs INSIDE the try so a
            // startup/migration failure — the most likely startup failure — enters maintenance mode
            // instead of crashing the host.
            Comment("Database initialization (migrations/EnsureCreated) — inside the try so a startup failure shows the maintenance page");
            RenderDatabaseInitialization();
            AppendLine();
            Comment("Configure all middleware pipelines");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.ConfigurePipeline(app);");
            AppendLine();
            Comment("Map all discovered endpoints");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.MapAllEndpoints(app);");
            AppendLine();
            Comment("Module endpoint hooks (SignalR hubs, gRPC services, etc.)");
            AppendLine("foreach (var configurator in app.Services.GetServices<global::Pragmatic.Composition.IEndpointRouteBuilderConfigurator>())");
            IncreaseIndent();
            AppendLine("configurator.Configure(app);");
            DecreaseIndent();
            AppendLine();
            RenderApiDocumentationMapping();
            RenderHostHealthMapping();
            // After every endpoint is mapped, because what they require is known only then: a host whose
            // endpoints require authorization and that can authenticate no one in this environment
            // refuses to start, instead of answering 500 to every protected request.
            Comment("Endpoints that require authorization need an authentication method in this environment");
            AppendLine("await global::Pragmatic.Composition.Hosting.AuthenticationRequirement.VerifyAsync(app, app.Environment);");
            AppendLine();
            AppendLine("await app.RunAsync();");
        });
        AppendLine("catch (Exception ex) when (maintenanceEnabled)");
        Block(() =>
        {
            Comment("Enter maintenance mode — build a fresh minimal app since the original may be disposed.");
            Comment("From the same args: the page must answer where the host was told to listen (--urls).");
            AppendLine("var maintenanceBuilder = WebApplication.CreateBuilder(args);");
            AppendLine("var maintenanceApp = maintenanceBuilder.Build();");
            AppendLine(
                "var maintenance = new MaintenanceMode(ex, pragmaticBuilder.Options.MaintenanceMode, builder.Environment.IsDevelopment());");
            AppendLine("maintenance.Configure(maintenanceApp);");
            AppendLine("await maintenanceApp.RunAsync();");
        });
    }

    /// <summary>
    ///     Publishes the builder's <c>MaintenanceModeOptions</c> to DI.
    /// </summary>
    /// <remarks>
    ///     The maintenance middleware is wired into every host and takes these options from the
    ///     container, and <c>UseMaintenanceMode()</c> is optional — without this, a host that never
    ///     calls it would have no instance to resolve. Registering the builder's own object also keeps the
    ///     middleware and the startup-failure page reading one instance instead of two.
    ///     <c>TryAdd</c>: <c>UseMaintenanceMode()</c> runs first, inside the configure callback.
    /// </remarks>
    private void RenderMaintenanceOptionsRegistration()
    {
        Comment("Maintenance options — one instance for the always-wired 503 middleware and the startup-failure page");
        AppendLine(
            "global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton(builder.Services, pragmaticBuilder.Options.MaintenanceMode);");
        AppendLine();
    }

    /// <summary>
    ///     Bridges the host health aggregator into the standard health check pipeline.
    /// </summary>
    /// <remarks>
    ///     This bridge is the only way the verdict of the registered <c>IHostHealthContributor</c>
    ///     instances reaches an endpoint — Messaging writes no <c>IHealthCheck</c> of its own because
    ///     it relies on it. On by default; <c>DisableHealthEndpoint()</c> turns it off, because mapping
    ///     a route is a decision about the host's route table.
    /// </remarks>
    private void RenderHostHealthRegistration()
    {
        Comment("Aggregated host health (IHostStatus + IHostHealthContributor) — on unless DisableHealthEndpoint()");
        AppendLine("if (pragmaticBuilder.Options.Health.Enabled)");
        Block(() =>
        {
            AppendLine(
                "var _healthChecks = global::Microsoft.Extensions.DependencyInjection.HealthCheckServiceCollectionExtensions.AddHealthChecks(builder.Services);");
            AppendLine(
                "global::Microsoft.Extensions.DependencyInjection.HealthChecksBuilderAddCheckExtensions.AddCheck<global::Pragmatic.Composition.ControlPlane.ControlPlaneHealthCheck>(_healthChecks, \"pragmatic-host\");");
        });
        AppendLine();
    }

    /// <summary>
    ///     Maps the aggregated health endpoint on the configured route, when enabled.
    /// </summary>
    /// <summary>
    ///     Publishes the compile-time API document, and Scalar over it when the host references Scalar.
    /// </summary>
    /// <remarks>
    ///     After every endpoint is mapped, so a document or reference the application mapped itself is
    ///     already in the route table and the mapper steps aside. Which environments publish is the
    ///     mapper's decision; whether Scalar exists is a fact of this compilation, stated here.
    /// </remarks>
    private void RenderApiDocumentationMapping()
    {
        var features = _model.DetectedFeatures;
        if (!features.HasEndpointsOpenApi)
            return;

        Comment("API contract — the compile-time document in Development, everywhere after UseApiDocumentation()");
        if (features.HasScalar)
        {
            Comment("Scalar is referenced: an interactive reference over the document, in Development");
            AppendLine("global::Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper.Map(app,");
            AppendLine("    new global::Pragmatic.Endpoints.OpenApi.InteractiveApiReference(\"/scalar\",");
            AppendLine("        static endpoints => global::Scalar.AspNetCore.ScalarEndpointRouteBuilderExtensions.MapScalarApiReference(endpoints)));");
        }
        else
        {
            AppendLine("global::Pragmatic.Endpoints.OpenApi.ApiDocumentationMapper.Map(app);");
        }

        AppendLine();
    }

    private void RenderHostHealthMapping()
    {
        // No `if (Enabled)` here: the mapper reads the same flag, and a condition written in two
        // places is a condition that will one day disagree with itself. The decision — enabled, path
        // taken, path free — belongs to the mapper, where it can be read and tested.
        Comment("Aggregated host health endpoint — mapped unless the application disabled it or owns the route");
        AppendLine(
            "global::Pragmatic.Composition.Hosting.HealthEndpointMapper.Map(app, pragmaticBuilder.Options.Health);");
        AppendLine();
    }

    /// <summary>
    ///     Wraps the database initialization block with maintenance mode activation and progress reporting.
    ///     When UseMaintenanceMode() is configured, the generated code:
    ///     1. Activates maintenance mode before DB init
    ///     2. Reports progress for each database (start, success, error)
    ///     3. Deactivates maintenance mode on completion (via using/dispose)
    /// </summary>
    private void RenderMaintenanceModeWrapper(Action renderDatabaseBlock)
    {
        AddUsing("Pragmatic.Maintenance");

        Comment("Maintenance mode — activate during database initialization if UseMaintenanceMode() was called");
        AppendLine("var maintenanceMode = app.Services.GetService<global::Pragmatic.Maintenance.IMaintenanceMode>();");
        AppendLine("var progressStream = app.Services.GetService<global::Pragmatic.Maintenance.IMigrationProgressStream>();");
        AppendLine();
        AppendLine("if (maintenanceMode is not null)");
        Block(() =>
        {
            AppendLine("using var _maintenanceHandle = maintenanceMode.Activate(\"Database initialization\", TimeSpan.FromMinutes(5));");
            AppendLine("var _initFailed = false;");
            AppendLine("try");
            Block(() => renderDatabaseBlock());
            AppendLine("catch (Exception ex)");
            Block(() =>
            {
                AppendLine("_initFailed = true;");
                // Terminal failure: emit an error event AND complete the stream so SSE/consumers
                // see the failure as the final event instead of a misleading "complete".
                AppendLine("progressStream?.ReportFailure(\"Database initialization failed: \" + ex.Message, ex);");
                AppendLine("throw;");
            });
            AppendLine("finally");
            Block(() =>
            {
                // ReportFailure already completed the stream on the error path; only emit the
                // success-complete when initialization actually succeeded.
                AppendLine("if (!_initFailed)");
                Block(() =>
                {
                    AppendLine("progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"complete\", \"Database initialization finished\", ProgressPercent: 100.0));");
                    AppendLine("progressStream?.Complete();");
                });
            });
        });
        AppendLine("else");
        Block(() => renderDatabaseBlock());
    }

    /// <summary>
    ///     Generates database initialization code that runs between Build() and RunAsync().
    ///     Uses MigrationDbContext types (one per physical database) which contain all entities.
    ///     Controlled by PragmaticOptions.EnsureDatabaseCreated / AutoMigrations flags.
    ///     When UseMaintenanceMode() is configured, wraps with maintenance activation + progress streaming.
    /// </summary>
    private void RenderDatabaseInitialization()
    {
        if (!_model.HasDatabaseRegistrations)
        {
            Comment("No databases configured — skip initialization");
            return;
        }

        // Group HostIncludes by database (same pattern as RegisterAllDatabases)
        var databaseGroups = _model.HostIncludes
            .Where(i => i.HasDatabase)
            .GroupBy(i => i.DatabaseTypeName ?? string.Empty)
            .ToList();

        if (databaseGroups.Count == 0)
        {
            Comment("No databases configured — skip initialization");
            return;
        }

        AddUsing("Microsoft.EntityFrameworkCore");

        RenderDeclaredConnectionStringChecks(databaseGroups);

        var migratable = MigratableGroups(databaseGroups);
        if (migratable.Count == 0)
        {
            Comment("No database has an entity behind it yet — nothing to migrate");
            return;
        }

        // Wrap the entire DB init with maintenance mode if available
        RenderMaintenanceModeWrapper(() => RenderDatabaseInitBlock(migratable));
    }

    /// <summary>
    ///     The databases with an entity behind them: the only ones with a schema and a migration context.
    /// </summary>
    /// <remarks>
    ///     The block below is rendered for these only. Rendered with none, it would open its branches,
    ///     declare its progress stream and fill neither — a CS0219 in a file the application cannot
    ///     edit, fatal under <c>-warnaserror</c>. The connection strings are checked for every database
    ///     regardless: a database the host declares is one it expects to reach.
    /// </remarks>
    private static List<IGrouping<string, HostIncludeModel>> MigratableGroups(
        List<IGrouping<string, HostIncludeModel>> databaseGroups)
        => databaseGroups.Where(g => g.First().MigrationDbContextFqn is not null).ToList();

    /// <summary>
    ///     Checks every connection string a database declares before the first line that uses one.
    /// </summary>
    /// <remarks>
    ///     The reads below turn an absent key into <c>""</c>, and the empty value fails wherever it is
    ///     first used, under that step's message. The key is known here, so it is checked here, by name.
    ///     An in-memory database has no connection string and is skipped. The migration key is checked
    ///     too: it is declared, and it is what migrations connect with.
    /// </remarks>
    private void RenderDeclaredConnectionStringChecks(List<IGrouping<string, HostIncludeModel>> databaseGroups)
    {
        var checks = databaseGroups
            .Select(g => g.First())
            .Where(d => d.DatabaseProvider != "InMemory")
            .SelectMany(d => new[] { d.DatabaseConfigKey, d.MigrationConfigKey }
                .Where(k => !string.IsNullOrEmpty(k))
                .Select(k => (Key: k!, Database: GetSimpleName(d.DatabaseTypeName ?? string.Empty))))
            .Distinct()
            .ToList();

        if (checks.Count == 0)
            return;

        Comment("Connection strings the databases declare — checked by name before anything connects");
        foreach (var (key, database) in checks)
            AppendLine(
                $"global::Pragmatic.Composition.Hosting.DeclaredConnectionStrings.Require({_appConfigExpr}, "
                + $"\"{key}\", \"{database}\", builder.Environment.ContentRootPath);");
        AppendLine();
    }

    /// <summary>
    ///     Renders the core database initialization block (used both with and without maintenance wrapper).
    /// </summary>
    private void RenderDatabaseInitBlock(List<IGrouping<string, HostIncludeModel>> databaseGroups)
    {
        // When Pragmatic.Migrations is referenced, generate branch: runner vs EF Core fallback
        if (_model.DetectedFeatures.HasMigrations)
        {
            RenderMigrationRunnerBranch(databaseGroups);
            return;
        }

        Comment("Database initialization — runs before hosted services (lookup preloaders, etc.)");
        AppendLine("if (pragmaticBuilder.Options.EnsureDatabaseCreated || pragmaticBuilder.Options.AutoMigrations)");
        Block(() =>
        {
            var dbIndex = 0;
            var totalDbs = databaseGroups.Count(g => g.First().MigrationDbContextFqn is not null);

            foreach (var dbGroup in databaseGroups)
            {
                var representative = dbGroup.First();
                var provider = representative.DatabaseProvider;
                var configKey = representative.DatabaseConfigKey;
                var dbSimpleName = GetSimpleName(dbGroup.Key);

                // Use the MigrationDbContext FQN resolved from compilation
                var migrationDbContextName = representative.MigrationDbContextFqn;
                if (migrationDbContextName is null)
                    continue; // MigrationDbContext not found — skip this database

                Comment($"Database: {dbSimpleName}");

                // Progress reporting
                var startPercent = totalDbs > 0 ? (double)dbIndex / totalDbs * 100 : 0;
                var endPercent = totalDbs > 0 ? (double)(dbIndex + 1) / totalDbs * 100 : 100;
                var startPctStr = startPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"Initializing {dbSimpleName}...\", ProgressPercent: {startPctStr}, DatabaseName: \"{dbSimpleName}\"));");

                // Build connection string resolution
                var connectionExpr = configKey is not null
                    ? $"{_appConfigExpr}[\"{configKey}\"]"
                    : $"\"\"";

                var useMethod = DatabaseProviderCall.UseMethod(provider);

                if (provider == "InMemory")
                {
                    AppendLine($"var {ToCamelCase(dbSimpleName)}Options = new DbContextOptionsBuilder<{migrationDbContextName}>()");
                    AppendLine($"    .{useMethod}(\"{dbSimpleName}\")");
                    AppendLine("    .Options;");
                }
                else
                {
                    AppendLine($"var {ToCamelCase(dbSimpleName)}Options = new DbContextOptionsBuilder<{migrationDbContextName}>()");
                    AppendLine($"    .{useMethod}({DatabaseProviderCall.Arguments(provider, connectionExpr, configKey)})");
                    AppendLine("    .Options;");
                }

                AppendLine($"using (var db = new {migrationDbContextName}({ToCamelCase(dbSimpleName)}Options))");
                Block(() =>
                {
                    AppendLine("if (pragmaticBuilder.Options.EnsureDatabaseCreated)");
                    Block(() => AppendLine("await db.Database.EnsureCreatedAsync();"));
                    AppendLine("else");
                    Block(() => AppendLine("await db.Database.MigrateAsync();"));
                });

                var endPctStr = endPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
                AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"{dbSimpleName} OK\", ProgressPercent: {endPctStr}, DatabaseName: \"{dbSimpleName}\"));");
                AppendLine();

                dbIndex++;
            }
        });
    }

    /// <summary>
    ///     When Pragmatic.Migrations is referenced, generates a branch:
    ///     - If IMigrationRunner is registered → use declarative diff+apply
    ///     - Else → fallback to EF Core MigrateAsync/EnsureCreatedAsync
    /// </summary>
    /// <summary>
    ///     After the host's own database is migrated, applies the same schema to every tenant that
    ///     has a dedicated database. The orchestrator has always been registered by
    ///     <c>AddDbPerTenant</c> but nothing ever called it, so DB-per-tenant deployments started
    ///     with tenant databases stuck on the previous schema.
    /// </summary>
    /// <remarks>
    ///     Resolved with GetService and skipped when absent: hosts without multi-tenancy must not
    ///     take a dependency on it.
    /// </remarks>
    private void RenderTenantMigration(string dbSimpleName, string schemaClassName)
    {
        Comment($"Per-tenant databases for {dbSimpleName} (only when DB-per-tenant is enabled)");
        var orchestratorVar = $"{ToCamelCase(dbSimpleName)}TenantOrchestrator";
        AppendLine($"var {orchestratorVar} = {_appServicesExpr}.GetService<global::Pragmatic.Migrations.Tenant.ITenantMigrationOrchestrator>();");
        AppendLine($"if ({orchestratorVar} is not null)");
        Block(() =>
        {
            var summaryVar = $"{ToCamelCase(dbSimpleName)}TenantSummary";
            AppendLine($"var {summaryVar} = await {orchestratorVar}.MigrateAllTenantsAsync(");
            AppendLine($"    {schemaClassName}.Current, migrationOptions, default);");

            // Same fail-fast contract as the host database: a tenant left on an old schema would
            // otherwise serve requests against a database the code no longer matches.
            AppendLine($"if ({summaryVar}.FailureCount > 0)");
            Block(() =>
            {
                AppendLine($"throw new global::System.InvalidOperationException(\"[{dbSimpleName}] Tenant migration failed for \" + {summaryVar}.FailureCount + \" of \" + {summaryVar}.TotalTenants + \" tenant(s) — host startup aborted.\");");
            });

            AppendLine($"if ({summaryVar}.TotalTenants > 0)");
            Block(() =>
            {
                AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"{dbSimpleName}: \" + {summaryVar}.SuccessCount + \" tenant database(s) migrated\", DatabaseName: \"{dbSimpleName}\"));");
            });
        });
        AppendLine();
    }

    private void RenderMigrationRunnerBranch(List<IGrouping<string, HostIncludeModel>> databaseGroups)
    {
        AddUsing("Pragmatic.Migrations.Runner");

        Comment("Pragmatic Migrations — declarative schema diff when registered, EF Core fallback otherwise");
        AppendLine($"var migrationRunner = {_appServicesExpr}.GetService<global::Pragmatic.Migrations.Runner.IMigrationRunner>();");
        AppendLine($"var migrationOptions = {_appServicesExpr}.GetService<global::Pragmatic.Migrations.Runner.MigrationOptions>() ?? new global::Pragmatic.Migrations.Runner.MigrationOptions();");
        AppendLine("if (migrationRunner is not null)");
        Block(() =>
        {
            Comment("Use Pragmatic Migrations: introspect → diff → generate SQL → apply");
            foreach (var dbGroup in databaseGroups)
            {
                var representative = dbGroup.First();
                var configKey = representative.DatabaseConfigKey;
                var migrationKey = representative.MigrationConfigKey ?? configKey;
                var dbSimpleName = GetSimpleName(dbGroup.Key);

                if (configKey is null) continue;

                // No entity behind the database, so no {Db}Schema to migrate to.
                if (representative.MigrationDbContextFqn is null) continue;

                Comment($"Migrate: {dbSimpleName}");
                AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"Migrating {dbSimpleName}...\", DatabaseName: \"{dbSimpleName}\"));");

                // SchemaMetadata namespace derives from module namespace root (e.g. Showcase), not host (e.g. Showcase.Host)
                var moduleNs = representative.ModuleTypeName?.Replace("global::", "") ?? "";
                var schemaNs = moduleNs.Contains('.') ? moduleNs.Substring(0, moduleNs.LastIndexOf('.')) : moduleNs;
                if (schemaNs.Contains('.'))
                    schemaNs = schemaNs.Substring(0, schemaNs.LastIndexOf('.'));
                var schemaClassName = $"global::{schemaNs}.{dbSimpleName}Schema";

                // Runner creates and manages its own connection via IConnectionFactory (per-provider)
                // Use MigrationConfigKey (DDL privileges) if set, otherwise fall back to ConfigKey
                AppendLine($"var {ToCamelCase(dbSimpleName)}Result = await migrationRunner.MigrateAsync(");
                AppendLine($"    new global::Pragmatic.Migrations.Runner.MigrationContext(");
                AppendLine($"        {_appConfigExpr}[\"{migrationKey}\"] ?? \"\",");
                AppendLine($"        {schemaClassName}.Current,");
                AppendLine($"        migrationOptions),");
                AppendLine($"    default);");

                // Fail-fast: a failed migration (breaking changes blocked, or SQL error)
                // must abort host startup — never start on a stale/partial schema.
                AppendLine($"if (!{ToCamelCase(dbSimpleName)}Result.Success)");
                Block(() =>
                {
                    AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"error\", \"{dbSimpleName} migration failed\", IsError: true, DatabaseName: \"{dbSimpleName}\"));");
                    AppendLine($"throw new global::System.InvalidOperationException(\"[{dbSimpleName}] Migration failed — host startup aborted. \" + ({ToCamelCase(dbSimpleName)}Result.Error ?? \"unknown error\"));");
                });

                AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"{dbSimpleName}: \" + {ToCamelCase(dbSimpleName)}Result.ChangesApplied + \" changes\", DatabaseName: \"{dbSimpleName}\"));");
                AppendLine();

                RenderTenantMigration(dbSimpleName, schemaClassName);
            }
        });

        AppendLine("else if (pragmaticBuilder.Options.EnsureDatabaseCreated || pragmaticBuilder.Options.AutoMigrations)");
        Block(() =>
        {
            Comment("Fallback: EF Core database initialization");
            RenderEfCoreFallback(databaseGroups);
        });
    }

    /// <summary>
    ///     Renders the standard EF Core MigrateAsync/EnsureCreatedAsync block (extracted for reuse).
    /// </summary>
    private void RenderEfCoreFallback(List<IGrouping<string, HostIncludeModel>> databaseGroups)
    {
        var dbIndex = 0;
        var totalDbs = databaseGroups.Count(g => g.First().MigrationDbContextFqn is not null);

        foreach (var dbGroup in databaseGroups)
        {
            var representative = dbGroup.First();
            var provider = representative.DatabaseProvider;
            var configKey = representative.DatabaseConfigKey;
            var dbSimpleName = GetSimpleName(dbGroup.Key);
            var migrationDbContextName = representative.MigrationDbContextFqn;
            if (migrationDbContextName is null) continue;

            Comment($"Database: {dbSimpleName}");
            var startPercent = totalDbs > 0 ? (double)dbIndex / totalDbs * 100 : 0;
            var endPercent = totalDbs > 0 ? (double)(dbIndex + 1) / totalDbs * 100 : 100;
            var startPctStr = startPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"Initializing {dbSimpleName}...\", ProgressPercent: {startPctStr}, DatabaseName: \"{dbSimpleName}\"));");

            var connectionExpr = configKey is not null ? $"{_appConfigExpr}[\"{configKey}\"]" : "\"\"";
            var useMethod = DatabaseProviderCall.UseMethod(provider);

            if (provider == "InMemory")
            {
                AppendLine($"var {ToCamelCase(dbSimpleName)}Options = new DbContextOptionsBuilder<{migrationDbContextName}>()");
                AppendLine($"    .{useMethod}(\"{dbSimpleName}\")");
                AppendLine("    .Options;");
            }
            else
            {
                AppendLine($"var {ToCamelCase(dbSimpleName)}Options = new DbContextOptionsBuilder<{migrationDbContextName}>()");
                AppendLine($"    .{useMethod}({DatabaseProviderCall.Arguments(provider, connectionExpr, configKey)})");
                AppendLine("    .Options;");
            }

            AppendLine($"using (var db = new {migrationDbContextName}({ToCamelCase(dbSimpleName)}Options))");
            Block(() =>
            {
                AppendLine("if (pragmaticBuilder.Options.EnsureDatabaseCreated)");
                Block(() => AppendLine("await db.Database.EnsureCreatedAsync();"));
                AppendLine("else");
                Block(() => AppendLine("await db.Database.MigrateAsync();"));
            });

            var endPctStr = endPercent.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            AppendLine($"progressStream?.Report(new global::Pragmatic.Maintenance.MigrationProgressEvent(\"migration\", \"{dbSimpleName} OK\", ProgressPercent: {endPctStr}, DatabaseName: \"{dbSimpleName}\"));");
            AppendLine();
            dbIndex++;
        }
    }

    private void RenderTelemetrySetup(string servicesExpr, string optionsExpr, string isDevelopmentExpr,
        string? configExpr = null)
    {
        // Bind telemetry options from appsettings.json "Telemetry" section (if present)
        if (configExpr is not null)
        {
            Comment("Bind telemetry options from configuration (appsettings.json \"Telemetry\" section)");
            AppendLine($"{configExpr}.GetSection(\"Telemetry\").Bind({optionsExpr});");
            AppendLine();
        }

        Comment("OpenTelemetry — auto-configured, opt-out via options.Telemetry.Enabled = false");
        AppendLine($"global::Pragmatic.Composition.Hosting.PragmaticTelemetry.AddPragmaticTelemetry({servicesExpr}, {optionsExpr}, {isDevelopmentExpr});");
        AppendLine();
    }

    /// <summary>
    ///     Adds the generated [FastEnum] converters discovered from referenced assemblies.
    ///     <para>
    ///         Order matters and is the whole contract: <c>Converters</c> is consulted front to back and
    ///         <c>JsonStringEnumConverter</c> is a factory that accepts EVERY enum, so anything added
    ///         after it is unreachable. These go first and claim their own enum; every other enum still
    ///         falls through to the framework converter, which is why this is additive and not a
    ///         format change. The two are observationally identical — see
    ///         <c>FastEnumJsonConverterEquivalenceTests</c>.
    ///     </para>
    /// </summary>
    private void RenderFastEnumConverters()
    {
        var registrations = _model.Assemblies
            .SelectMany(a => a.Entries)
            .Where(e => e.Category == MetadataCategoryIds.FastEnumConverters
                        && !string.IsNullOrEmpty(e.RegistrationMethod))
            .Select(e => e.RegistrationMethod)
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        if (registrations.Count == 0)
            return;

        Comment("[FastEnum] converters — zero-reflection, and first so they win for their own enum.");
        foreach (var registration in registrations)
            AppendLine($"global::{registration}(jsonOptions.SerializerOptions);");
    }

    private void RenderJsonDefaults()
    {
        Comment("Pragmatic sensible defaults: JSON serialization (pipeline steps can override)");
        Comment("The TypeInfoResolver comes from the shared PragmaticJsonOptions seam (DI-injected so");
        Comment("host-registered source-generated contexts + the AOT fallback opt-out apply here too).");
        AppendLine("global::Pragmatic.Serialization.PragmaticJsonServiceCollectionExtensions.AddPragmaticJson(builder.Services);");
        AppendLine("builder.Services.AddOptions<global::Microsoft.AspNetCore.Http.Json.JsonOptions>()");
        IncreaseIndent();
        AppendLine(".Configure<global::Pragmatic.Serialization.PragmaticJsonOptions>((jsonOptions, pragmaticJson) =>");
        AppendLine("{");
        IncreaseIndent();
        AppendLine(
            "jsonOptions.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;");
        AppendLine(
            "jsonOptions.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;");
        RenderFastEnumConverters();
        AppendLine(
            "jsonOptions.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());");
        AppendLine(
            "jsonOptions.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;");
        AppendLine();
        AppendLine("var resolver = pragmaticJson.Build().TypeInfoResolver;");
        AppendLine("if (resolver is not null)");
        AppendLine("{");
        IncreaseIndent();

        if (_model.HasPersistenceSerialization)
        {
            Comment("Exclude infrastructure properties from API responses (TenantId, PersistenceId internals)");
            AppendLine("jsonOptions.SerializerOptions.TypeInfoResolver = System.Text.Json.Serialization.Metadata.JsonTypeInfoResolver");
            IncreaseIndent();
            AppendLine(".WithAddedModifier(resolver, global::Pragmatic.Persistence.Serialization.EntityJsonModifier.ExcludeInfrastructureProperties);");
            DecreaseIndent();
        }
        else
        {
            AppendLine("jsonOptions.SerializerOptions.TypeInfoResolver = resolver;");
        }

        DecreaseIndent();
        AppendLine("}");
        AppendLine();
        Comment("What this configuration wrote, so a generated response writer is used only while it still holds:");
        Comment("anything configured after this line that changes the bytes sends responses back to the serializer.");
        AppendLine("global::Pragmatic.Serialization.GeneratedJsonDefaults.Mark(");
        AppendLine("    jsonOptions.SerializerOptions, pragmaticJson,");
        AppendLine(_model.HasPersistenceSerialization
            ? "    excludesInfrastructure: resolver is not null);"
            : "    excludesInfrastructure: false);");
        DecreaseIndent();
        AppendLine("});");
        DecreaseIndent();
        AppendLine();
    }

    private void RenderRunWorkerAsyncBody()
    {
        AppendLine("var builder = Host.CreateApplicationBuilder(args);");
        AppendLine();

        if (_model.HasRequiredConfigValidation)
        {
            Comment("Validate required configuration sections (fail-fast before DI registration)");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.ValidateConfiguration(builder.Configuration);");
            AppendLine();
        }

        Comment("SG defaults — auto-register infrastructure modules");
        AppendLine($"global::{_model.RootNamespace}.PragmaticHost.RegisterAllPragmaticServices(builder.Services, builder.Configuration);");
        AppendLine();

        Comment("Module strategy configuration");
        AppendLine("var pragmaticBuilder = new PragmaticBuilder(builder.Services, builder.Configuration, builder.Environment);");
        AppendLine("configure?.Invoke(pragmaticBuilder);");
        AppendLine();

        RenderTelemetrySetup("builder.Services", "pragmaticBuilder.Options.Telemetry", "builder.Environment.IsDevelopment()", "builder.Configuration");

        if (_model.HasDatabaseRegistrations)
        {
            AppendLine("// Register DbContext instances for configured databases");
            AppendLine(
                $"global::{_model.RootNamespace}.PragmaticHost.RegisterAllDatabases(builder.Services, builder.Configuration);");
            AppendLine();
        }

        AppendLine("// Call ConfigureServices on all pipeline steps (business wiring)");
        AppendLine(
            $"global::{_model.RootNamespace}.PragmaticHost.CallConfigureServices(builder.Services, builder.Configuration, builder.Environment);");
        AppendLine();
        AppendLine("// Register [ServiceFactory] classes and their [Factory] methods");
        AppendLine(
            $"global::{_model.RootNamespace}.PragmaticServiceFactories.AddPragmaticServiceFactories(builder.Services);");

        // Last, deliberately: a [Decorator] has to be able to wrap a service that UseAuthorization or
        // an IStartupStep registered, and Decorate throws when there is nothing there yet.
        if (_model.HasDecorators)
        {
            AppendLine();
            AppendLine("// Apply [Decorator]s — after every other registration");
            AppendLine($"global::{_model.RootNamespace}.PragmaticHost.ApplyDecorators(builder.Services);");
        }
        AppendLine();
        AppendLine("var host = builder.Build();");
        AppendLine();
        RenderWorkerDatabaseInitialization();
        AppendLine("await host.RunAsync();");
    }

    /// <summary>
    ///     Worker-host database initialization. The generic host has no
    ///     WebApplication and no maintenance page, so it runs the migration/EnsureCreated block directly
    ///     against <c>host.Services</c> / <c>builder.Configuration</c>. A failed migration throws and
    ///     aborts worker startup (fail-fast — never run on a stale schema).
    /// </summary>
    private void RenderWorkerDatabaseInitialization()
    {
        if (!_model.HasDatabaseRegistrations)
            return;

        var databaseGroups = _model.HostIncludes
            .Where(i => i.HasDatabase)
            .GroupBy(i => i.DatabaseTypeName ?? string.Empty)
            .ToList();

        if (databaseGroups.Count == 0)
            return;

        AddUsing("Microsoft.EntityFrameworkCore");

        // Point the shared DB-init blocks at the worker host (no WebApplication "app").
        _appServicesExpr = "host.Services";
        _appConfigExpr = "builder.Configuration";

        RenderDeclaredConnectionStringChecks(databaseGroups);

        var migratable = MigratableGroups(databaseGroups);
        if (migratable.Count == 0)
        {
            Comment("No database has an entity behind it yet — nothing to migrate");
        }
        else
        {
            Comment("Database initialization (migrations/EnsureCreated) — workers migrate too");
            AppendLine("global::Pragmatic.Maintenance.IMigrationProgressStream? progressStream = null;");
            RenderDatabaseInitBlock(migratable);
            AppendLine();
        }

        // Restore defaults (the template instance is single-use, but keep it clean).
        _appServicesExpr = "app.Services";
        _appConfigExpr = "app.Configuration";
    }

    private static string GetSimpleName(string fullTypeName)
    {
        var clean = fullTypeName.Replace("global::", string.Empty);
        var lastDot = clean.LastIndexOf('.');
        return lastDot >= 0 ? clean.Substring(lastDot + 1) : clean;
    }

    private static string ToCamelCase(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }
}

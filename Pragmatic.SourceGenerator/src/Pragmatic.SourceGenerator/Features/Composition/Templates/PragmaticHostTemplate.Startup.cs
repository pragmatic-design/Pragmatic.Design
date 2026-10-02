// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Startup)

using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Partial: Startup wiring — RegisterAllPragmaticServices, ConfigureServices, ConfigurePipeline, ValidateConfiguration.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    private void RenderRegisterAllPragmaticServicesMethod()
    {
        XmlSummary("Registers all Pragmatic services: infrastructure modules, actions, repositories, and startup steps.");
        XmlParam("services", "The service collection.");
        XmlParam("configuration", "The application configuration.");
        XmlReturns("The service collection for chaining.");

        var parameters = new List<MethodParameter>
        {
            new("this IServiceCollection", "services"),
            new("IConfiguration", "configuration")
        };

        Method("RegisterAllPragmaticServices", () =>
        {
            // Control plane — always registered (NoOp default, replaced by the Agent backbone via UseAgent)
            RenderControlPlaneRegistrations();

            // ⚠️ The OpenAPI document counts as something to register, and leaving it out of this list
            // is how a host that serves only its contract registered nothing at all: the method returned
            // early and the document stayed in the process-wide static. Found by a generator test whose host carried a
            // manifest and no endpoint.
            var hasAnything = HasDomainModules || HasDiscoveredRepositories || HasDiscoveredServices ||
                              HasLocalServices || HasPipelineSteps || HasEndpoints || HasValidationMetadata ||
                              HasInfraModules || HasMessageHandlerMetadata || HasJobMetadata ||
                              HasSagaMetadata || HasTemporalBehaviorsMetadata || HasPrivacyAdapterMetadata ||
                              _model.HasOpenApiDocument;

            if (!hasAnything)
            {
                AppendLine("return services;");
                return;
            }

            // Remote boundary HTTP clients
            if (HasRemoteBoundaries)
                RenderRemoteBoundaryHttpClients();

            // Infrastructure modules -- auto-registered based on referenced assemblies
            if (HasInfraModules)
                RenderInfraModuleRegistrations();

            // Validators -- auto-registered when any assembly has [Validator] implementations
            if (HasValidationMetadata)
            {
                // ⚠️ Not from a module this host only reaches over HTTP: a validator of that module's
                // DTO takes that module's repositories, which are not registered here — and correctly
                // so, since the module has no database in this process. Measured on
                // Showcase.Billing.Host the first time anything started it.
                var validationEntries = HostedRegistrationMethodsFor(MetadataCategoryIds.Validation);

                if (validationEntries.Count > 0)
                {
                    Comment("Validators from the assemblies this host hosts");
                    foreach (var regMethod in validationEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Pipeline steps ([StartupStep]) discovered in referenced boundary libraries. Without this call
            // to the library-generated AddPipelineSteps, a module's [StartupStep] would silently never be
            // registered (neither ConfigureServices nor ConfigurePipeline would run).
            var discoveredStartupEntries = _model.Assemblies
                .SelectMany(a => a.Entries)
                .Where(e => e.Category == MetadataCategoryIds.Startup && !string.IsNullOrEmpty(e.RegistrationMethod))
                .Select(e => e.RegistrationMethod)
                .Distinct()
                .OrderBy(m => m)
                .ToList();

            if (discoveredStartupEntries.Count > 0)
            {
                Comment("Pipeline steps from discovered boundary libraries ([StartupStep])");
                foreach (var regMethod in discoveredStartupEntries)
                {
                    var lastDot = regMethod!.LastIndexOf('.');
                    var className = regMethod.Substring(0, lastDot);
                    var methodName = regMethod.Substring(lastDot + 1);
                    AppendLine($"global::{className}.{methodName}(services);");
                }

                AppendLine();
            }

            // Authorization catalog -- feeds each referenced assembly's generated PermissionRegistry.All /
            // RoleRegistry.All into DefaultPermissionCatalog (registered individually so they aggregate).
            if (HasAuthorizationCatalogMetadata)
            {
                var authCatalogEntries = _model.Assemblies
                    .SelectMany(a => a.Entries)
                    .Where(e => e.Category == MetadataCategoryIds.Authorization && !string.IsNullOrEmpty(e.RegistrationMethod))
                    .Select(e => e.RegistrationMethod)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                if (authCatalogEntries.Count > 0)
                {
                    Comment("Permission/role catalog entries from discovered assemblies");
                    foreach (var regMethod in authCatalogEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Privacy adapters -- the generated IPersonalDataSource / IErasureStep /
            // IProcessingActivitySource for every assembly that classifies personal data. The runtime
            // services take IEnumerable<> of each, so a missing call is not an error: the access request
            // returns nothing and the erasure erases nothing, both reporting success.
            if (HasPrivacyAdapterMetadata)
            {
                var privacyEntries = _model.Assemblies
                    .SelectMany(a => a.Entries)
                    .Where(e => e.Category == MetadataCategoryIds.PersonalData && !string.IsNullOrEmpty(e.RegistrationMethod))
                    .Select(e => e.RegistrationMethod)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                if (privacyEntries.Count > 0)
                {
                    Comment("Personal-data sources, erasure steps and processing activities from discovered assemblies");
                    foreach (var regMethod in privacyEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Configuration catalogue -- what every composed assembly declares it can be configured
            // with. Schema only, never values, so a [Sensitive] key is named and its content is not.
            if (HasConfigurationCatalogMetadata)
            {
                var catalogEntries = _model.Assemblies
                    .SelectMany(a => a.Entries)
                    .Where(e => e.Category == MetadataCategoryIds.Configuration && !string.IsNullOrEmpty(e.RegistrationMethod))
                    .Select(e => e.RegistrationMethod)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                if (catalogEntries.Count > 0)
                {
                    Comment("[Configuration] sections from discovered assemblies: bound, and catalogued");
                    foreach (var regMethod in catalogEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services, configuration);");
                    }

                    AppendLine();
                }
            }

            // Domain event bus. The publisher needs it, not only the handler: an operation that dispatches
            // resolves IDomainEventDispatcher whether or not anybody listens, and Identity.Local's sign-in
            // is one. Registering it only for handlers failed every sign-in of an application that had
            // none. Inert with no handler behind it.
            if (HasEventHandlerMetadata || _model.DetectedFeatures.HasEvents)
            {
                AddUsing("Pragmatic.Events.Extensions");
                Comment("Domain event bus (in-memory) — publishers resolve it with or without handlers");
                AppendLine("services.AddInMemoryDomainEvents();");
                AppendLine();
            }

            if (HasEventHandlerMetadata)
            {
                // Register event handlers from each assembly this host hosts
                var eventHandlerEntries = HostedRegistrationMethodsFor(MetadataCategoryIds.EventHandlers);

                if (eventHandlerEntries.Count > 0)
                {
                    Comment("Event handlers from discovered assemblies");
                    foreach (var regMethod in eventHandlerEntries)
                    {
                        // regMethod = "Namespace.ClassName.MethodName" -- split into class + method
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // This host's compile-time OpenAPI document, in this host's container.
            //
            // ⚠️ Read from a process-wide static that a [ModuleInitializer] writes per host, two hosts
            // in one process would make the one loaded second answer for both — a service's
            // GET /openapi/v1.json returning the other service's document. The static stays as the
            // fallback for a host that registers nothing; this line is what makes the answer belong to
            // the host that was asked. `PragmaticOpenApi` is this assembly's own
            // generated class, written by this same generator in this same compilation.
            if (_model.HasOpenApiDocument)
            {
                Comment("This host's compile-time OpenAPI document: per host, not per process");
                // ⚠️ Both values from PragmaticOpenApi, which is this host's own generated class. Reading
                // the flag from PragmaticOpenApiRegistry — the process-wide static — would, with two
                // hosts, publish the other one's security requirement.
                AppendLine(
                    "services.AddSingleton(new global::Pragmatic.Endpoints.OpenApi.HostOpenApiDocument("
                    + "PragmaticOpenApi.Json, PragmaticOpenApi.RequiresAuthentication));");
                AppendLine();
            }

            // This host's aggregated manifest, in this host's container — the same reading as
            // the document above applied to the other registry the generator writes per host.
            //
            // ⚠️ ManifestRegistry accumulates rather than overwrites, so the symptom is not a lost
            // manifest but a shared one: the runtime document's transformer built its endpoint lookup
            // from every host in the process, and `requiresAuthentication` — which decides whether the
            // document declares security schemes at all — is computed over the whole lookup.
            // `PragmaticManifest` is this assembly's own generated class, from this same compilation.
            if (_model.HasAggregatedManifest)
            {
                Comment("This host's aggregated manifest: per host, not per process");
                AppendLine(
                    "services.AddSingleton(new global::Pragmatic.Endpoints.OpenApi.HostManifest("
                    + "PragmaticManifest.Json));");
                AppendLine();
            }

            // Message handlers -- auto-registered when any assembly has [MessageHandler] implementations
            if (HasMessageHandlerMetadata)
            {
                var messageHandlerEntries = HostedRegistrationMethodsFor(MetadataCategoryIds.MessageHandlers);

                if (messageHandlerEntries.Count > 0)
                {
                    Comment("Message handlers from the assemblies this host hosts");
                    foreach (var regMethod in messageHandlerEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Job registrations -- auto-registered when any assembly has [Job] or [RecurringJob] implementations
            if (HasJobMetadata)
            {
                var jobEntries = HostedRegistrationMethodsFor(MetadataCategoryIds.Jobs);

                if (jobEntries.Count > 0)
                {
                    Comment("Job classes from the assemblies this host hosts");
                    foreach (var regMethod in jobEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Saga registrations -- auto-registered when any assembly has [Saga<TState>] implementations
            if (HasSagaMetadata)
            {
                var sagaEntries = HostedRegistrationMethodsFor(MetadataCategoryIds.Sagas);

                if (sagaEntries.Count > 0)
                {
                    Comment("Sagas from the assemblies this host hosts");
                    foreach (var regMethod in sagaEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Generated RollUpRule registrations. Same shape as the redaction block below, and the
            // same defect before it existed: the rules were emitted, a method registered them, the
            // interceptor asked the container for them — and nothing called the method, so every
            // [RollUp] aggregate in a generated application stayed at its default.
            if (HasRollUpRuleMetadata)
            {
                Comment("Generated [RollUp] rules from discovered assemblies — the interceptor reads these");
                foreach (var regMethod in RegistrationMethodsFor(MetadataCategoryIds.RollUpRules))
                {
                    var lastDot = regMethod.LastIndexOf('.');
                    AppendLine($"global::{regMethod.Substring(0, lastDot)}.{regMethod.Substring(lastDot + 1)}(services);");
                }

                AppendLine();
            }

            // Generated read contracts ([Published] → I{Module}Reads). Same shape as the block above,
            // and the same defect before it existed: the contract was generated, its registration was
            // generated, and nothing called it — so the action injecting it failed at resolution, on
            // the first request rather than at startup.
            if (HasReadContractMetadata)
            {
                Comment("Generated read contracts ([Published] queries) from discovered assemblies");
                foreach (var regMethod in RegistrationMethodsFor(MetadataCategoryIds.ReadContracts))
                {
                    var lastDot = regMethod.LastIndexOf('.');
                    AppendLine($"global::{regMethod.Substring(0, lastDot)}.{regMethod.Substring(lastDot + 1)}(services);");
                }

                AppendLine();
            }

            // Generated IRedactionMap registrations. Without this block the maps were emitted, the
            // registration methods were emitted, and nothing called them — so [NotLogged] and
            // [PersonalData] marked a field and changed nothing.
            if (HasRedactionMetadata)
            {
                Comment("Generated redaction maps ([NotLogged] / [PersonalData]) from discovered assemblies");
                var redactionEntries = RegistrationMethodsFor(MetadataCategoryIds.Redaction);

                foreach (var regMethod in redactionEntries)
                {
                    var lastDot = regMethod.LastIndexOf('.');
                    AppendLine($"global::{regMethod.Substring(0, lastDot)}.{regMethod.Substring(lastDot + 1)}(services);");
                }

                if (redactionEntries.Count > 0)
                {
                    // Registering the maps is not the effect. This wraps every logger provider already
                    // in the container so the marked members are masked before any of them formats an
                    // entry — Serilog, the console provider, a test collector, whatever is there.
                    // Masking only inside Pragmatic's own providers would make a safety guarantee
                    // conditional on adopting a particular console writer.
                    AppendLine("global::Pragmatic.Redaction.DeclaredRedactionServiceCollectionExtensions.AddDeclaredRedaction(services);");
                    AppendLine();
                }
            }

            // Generated JsonSerializerContext registrations -- when any assembly opted into W3 JSON generation
            if (HasJsonContextMetadata)
            {
                Comment("Generated JSON contexts from discovered assemblies (AOT serialization)");
                var jsonContextEntries = _model.Assemblies
                    .SelectMany(a => a.Entries)
                    .Where(e => e.Category == MetadataCategoryIds.JsonContexts && !string.IsNullOrEmpty(e.RegistrationMethod))
                    .Select(e => e.RegistrationMethod)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                if (jsonContextEntries.Count > 0)
                {
                    foreach (var regMethod in jsonContextEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // Temporal JSON behaviors -- registered when any assembly declares timezone conversion attributes
            if (HasTemporalBehaviorsMetadata)
            {
                Comment("Temporal timezone behaviors from discovered assemblies");
                var temporalBehaviorEntries = _model.Assemblies
                    .SelectMany(a => a.Entries)
                    .Where(e => e.Category == MetadataCategoryIds.TemporalBehaviors && !string.IsNullOrEmpty(e.RegistrationMethod))
                    .Select(e => e.RegistrationMethod)
                    .Distinct()
                    .OrderBy(m => m)
                    .ToList();

                if (temporalBehaviorEntries.Count > 0)
                {
                    foreach (var regMethod in temporalBehaviorEntries)
                    {
                        var lastDot = regMethod.LastIndexOf('.');
                        var className = regMethod.Substring(0, lastDot);
                        var methodName = regMethod.Substring(lastDot + 1);
                        AppendLine($"global::{className}.{methodName}(services);");
                    }

                    AppendLine();
                }
            }

            // DI services from referenced assemblies (generated directly from enriched metadata)
            if (HasDiscoveredServices)
                RenderDiscoveredServiceRegistrations();

            // Local host services
            if (HasLocalServices)
                RenderLocalServiceRegistrations();

            // Domain actions
            if (HasDomainModules || HasDiscoveredActions)
            {
                Comment("Domain actions");
                AppendLine("services.RegisterAllDomainActions();");
                AppendLine();
            }

            // Repositories
            if (HasDiscoveredRepositories)
            {
                Comment("Repositories");
                AppendLine("services.RegisterAllRepositories();");
                AppendLine();
            }

            // Endpoints
            if (HasEndpoints)
            {
                Comment("Endpoints");
                AppendLine("services.RegisterAllEndpoints();");
                AppendLine();
            }

            // Pipeline steps
            if (HasPipelineSteps)
            {
                Comment("Startup steps");
                AppendLine("services.RegisterAllPipelineSteps();");
                AppendLine();
            }

            AppendLine("return services;");
        }, "IServiceCollection", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    // =========================================================================
    // Direct DI Registration (from enriched metadata)
    // =========================================================================

    private void RenderDiscoveredServiceRegistrations()
    {
        var needsActivatorUtilities = _model.DiscoveredServices.Any(s => s.Factory is not null);
        if (needsActivatorUtilities)
            AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");

        // Group services by source assembly for readability
        var grouped = _model.DiscoveredServices
            .GroupBy(s => s.SourceAssembly)
            .OrderBy(g => g.Key);

        foreach (var group in grouped)
        {
            Comment($"Services from {group.Key}");
            foreach (var service in group.OrderBy(s => s.Implementation))
                RenderDiscoveredServiceRegistration(service);
        }

        // Decorators are NOT applied here — see RenderApplyDecoratorsMethod.

        AppendLine();
    }

    private void RenderDiscoveredServiceRegistration(DiscoveredServiceInfo service)
    {
        if (service is { IsOpenGeneric: true, OpenGenericInterface: not null, OpenGenericImplementation: not null })
        {
            var method = GetRegistrationMethodName(service.Lifetime);
            AppendLine($"services.{method}({service.OpenGenericInterface}, {service.OpenGenericImplementation});");
        }
        else if (service.Factory is not null)
        {
            RenderDiscoveredFactoryRegistration(service);
        }
        else if (service.Key is not null)
        {
            AppendLine(
                $"services.AddKeyed{service.Lifetime}<{service.Interface}, {service.Implementation}>(\"{service.Key}\");");
        }
        else
        {
            var method = GetRegistrationMethodName(service.Lifetime);
            AppendLine($"services.{method}<{service.Interface}, {service.Implementation}>();");
        }
    }

    private void RenderDiscoveredFactoryRegistration(DiscoveredServiceInfo service)
    {
        var method = GetRegistrationMethodName(service.Lifetime);
        var parts = new List<string>();

        parts.Add($"var instance = ActivatorUtilities.CreateInstance<{service.Implementation}>(sp);");

        if (service.Factory is not null)
        {
            foreach (var prop in service.Factory.PropertyInjections)
            {
                if (prop.Key is not null)
                {
                    var keyedMethod = prop.IsRequired ? "GetRequiredKeyedService" : "GetKeyedService";
                    parts.Add($"instance.{prop.PropertyName} = sp.{keyedMethod}<{prop.PropertyType}>(\"{prop.Key}\");");
                }
                else if (prop.IsRequired)
                {
                    parts.Add($"instance.{prop.PropertyName} = sp.GetRequiredService<{prop.PropertyType}>();");
                }
                else
                {
                    parts.Add($"instance.{prop.PropertyName} = sp.GetService<{prop.PropertyType}>();");
                }
            }

            foreach (var method2 in service.Factory.MethodInjections)
            {
                var args = string.Join(", ", method2.Parameters.Select(p =>
                {
                    if (p.Key is not null)
                    {
                        var keyedMethod = p.IsOptional ? "GetKeyedService" : "GetRequiredKeyedService";
                        return $"sp.{keyedMethod}<{p.Type}>(\"{p.Key}\")";
                    }

                    return p.IsOptional
                        ? $"sp.GetService<{p.Type}>()"
                        : $"sp.GetRequiredService<{p.Type}>()";
                }));
                parts.Add($"instance.{method2.MethodName}({args});");
            }
        }

        parts.Add("return instance;");

        var factoryBody = string.Join(" ", parts);
        if (service.Key is not null)
            // Keyed factory: preserve the key on a factory-registered ([Inject]) service.
            AppendLine($"services.AddKeyed{service.Lifetime}<{service.Interface}>(\"{service.Key}\", (sp, _) => {{ {factoryBody} }});");
        else
            AppendLine($"services.{method}<{service.Interface}>(sp => {{ {factoryBody} }});");
    }

    private void RenderLocalServiceRegistrations()
    {
        var needsActivatorUtilities = _model.LocalServices.Any(s => s.RequiresFactory);
        if (needsActivatorUtilities)
            AddUsing("Microsoft.Extensions.DependencyInjection.Extensions");

        Comment("Local host services");
        foreach (var service in _model.LocalServices.OrderBy(s => s.FullTypeName))
            RenderLocalServiceRegistration(service);

        // Decorators are NOT applied here — see RenderApplyDecoratorsMethod.

        AppendLine();
    }

    private void RenderLocalServiceRegistration(ServiceModel service)
    {
        if (service.IsOpenGeneric)
        {
            var method = GetRegistrationMethodName(service.Lifetime);
            AppendLine(
                $"services.{method}({service.OpenGenericServiceTypeName}, {service.OpenGenericImplementationTypeName});");
        }
        else if (service.RequiresFactory)
        {
            RenderLocalFactoryRegistration(service);
        }
        else if (service.Key is not null)
        {
            AppendLine(
                $"services.AddKeyed{service.Lifetime}<{service.ServiceTypeName}, {service.FullTypeName}>(\"{service.Key}\");");
        }
        else
        {
            var method = GetRegistrationMethodName(service.Lifetime);
            AppendLine($"services.{method}<{service.ServiceTypeName}, {service.FullTypeName}>();");
        }
    }

    private void RenderLocalFactoryRegistration(ServiceModel service)
    {
        var method = GetRegistrationMethodName(service.Lifetime);
        var parts = new List<string>();

        parts.Add($"var instance = ActivatorUtilities.CreateInstance<{service.FullTypeName}>(sp);");

        foreach (var prop in service.PropertyInjections)
        {
            if (prop.Key is not null)
            {
                var keyedMethod = prop.IsRequired ? "GetRequiredKeyedService" : "GetKeyedService";
                parts.Add(
                    $"instance.{prop.PropertyName} = sp.{keyedMethod}<{prop.PropertyTypeName}>(\"{prop.Key}\");");
            }
            else if (prop.IsRequired)
            {
                parts.Add($"instance.{prop.PropertyName} = sp.GetRequiredService<{prop.PropertyTypeName}>();");
            }
            else
            {
                parts.Add($"instance.{prop.PropertyName} = sp.GetService<{prop.PropertyTypeName}>();");
            }
        }

        foreach (var method2 in service.MethodInjections)
        {
            var args = string.Join(", ", method2.Parameters.Select(p =>
            {
                if (p.Key is not null)
                {
                    var keyedMethod = p.IsOptional ? "GetKeyedService" : "GetRequiredKeyedService";
                    return $"sp.{keyedMethod}<{p.FullTypeName}>(\"{p.Key}\")";
                }

                return p.IsOptional
                    ? $"sp.GetService<{p.FullTypeName}>()"
                    : $"sp.GetRequiredService<{p.FullTypeName}>()";
            }));
            parts.Add($"instance.{method2.MethodName}({args});");
        }

        parts.Add("return instance;");

        var factoryBody = string.Join(" ", parts);
        if (service.Key is not null)
            // Keyed factory: preserve the key on a factory-registered ([Inject]) service.
            AppendLine($"services.AddKeyed{service.Lifetime}<{service.ServiceTypeName}>(\"{service.Key}\", (sp, _) => {{ {factoryBody} }});");
        else
            AppendLine($"services.{method}<{service.ServiceTypeName}>(sp => {{ {factoryBody} }});");
    }

    private static string GetRegistrationMethodName(string lifetime) =>
        lifetime switch
        {
            "Singleton" => "AddSingleton",
            "Scoped" => "AddScoped",
            "Transient" => "AddTransient",
            _ => "AddScoped"
        };

    /// <summary>
    ///     Emits <c>ApplyDecorators</c>, the last registration pass.
    /// </summary>
    /// <remarks>
    ///     Decoration inline with the registrations would run before the <c>Use*()</c> callback and
    ///     before any <c>IStartupStep.ConfigureServices</c>. A decorator on a service the framework
    ///     registers later — <c>IUserScopeResolver</c>, installed by <c>UseAuthorization</c> — would
    ///     find nothing to wrap, and <c>Decorate</c> throws: "Cannot decorate IUserScopeResolver: no
    ///     registrations found", at startup, on a build that compiled. The workaround — writing
    ///     <c>services.Decorate&lt;&gt;</c> by hand in a step of its own — is the whole of what
    ///     <c>[Decorator]</c> is supposed to spare a consumer.
    ///     <para>
    ///         Running last also means a decorator sees every registration, whoever made it — the
    ///         guarantee the attribute always implied.
    ///     </para>
    /// </remarks>
    private void RenderApplyDecoratorsMethod()
    {
        var discovered = _model.DiscoveredDecorators.IsDefaultOrEmpty
            ? []
            : _model.DiscoveredDecorators.OrderBy(d => d.Interface).ThenBy(d => d.Order).ToList();
        var local = _model.LocalDecorators.IsDefaultOrEmpty
            ? []
            : _model.LocalDecorators.OrderBy(d => d.DecoratedInterface).ThenBy(d => d.Order).ToList();

        if (discovered.Count == 0 && local.Count == 0)
            return;

        AddUsing("Pragmatic.Composition.Extensions");

        XmlSummary("Applies every [Decorator], after all other registration has run.");
        XmlParam("services", "The service collection.");

        Method("ApplyDecorators", () =>
        {
            foreach (var decorator in discovered)
                AppendLine($"services.Decorate<{decorator.Interface}, {decorator.Implementation}>();");
            foreach (var decorator in local)
                AppendLine($"services.Decorate<{decorator.DecoratedInterface}, {decorator.FullTypeName}>();");
        }, "void", new List<MethodParameter> { new("global::Microsoft.Extensions.DependencyInjection.IServiceCollection", "services") },
            modifiers: new MethodModifiers { IsStatic = true });

        AppendLine();
    }

    private void RenderCallConfigureServicesMethod()
    {
        XmlSummary("Calls ConfigureServices on all discovered IStartupStep implementations.");

        var configureParams = new List<MethodParameter>
        {
            new("IServiceCollection", "services"),
            new("IConfiguration", "configuration"),
            new("IHostEnvironment", "environment")
        };

        Method("CallConfigureServices", () =>
        {
            // Honor IStartupStep.ConfigureServices for EVERY step. By the
            // time this runs, RegisterAllPragmaticServices has registered them all as IStartupStep — host
            // [StartupStep], [NeedsStep], built-in, AND the boundary-library steps via AddPipelineSteps — so
            // resolving IStartupStep from a bootstrap provider covers them uniformly (their constructor
            // dependencies resolve too, so a step may take constructor parameters).
            // Order by the runtime IStartupStep.Order, the same key ConfigurePipeline uses.
            Comment("Honor ConfigureServices for all discovered IStartupStep (host + NeedsStep + built-in + module), ordered by Order.");
            AppendLine("using var _bootstrap = services.BuildServiceProvider();");
            AppendLine("foreach (var startupStep in global::Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<IStartupStep>(_bootstrap).OrderBy(s => s.Order))");
            IncreaseIndent();
            AppendLine("startupStep.ConfigureServices(services, configuration, environment);");
            DecreaseIndent();
        }, "void", configureParams, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    private void RenderConfigurePipelineMethod()
    {
        XmlSummary("Configures all middleware from discovered IStartupStep implementations.");

        var configureParams = new List<MethodParameter>
        {
            new("IApplicationBuilder", "app")
        };

        Method("ConfigurePipeline", () =>
        {
            AppendLine("var steps = app.ApplicationServices");
            AppendLine("    .GetServices<IStartupStep>()");
            AppendLine("    .OrderBy(s => s.Order)");
            AppendLine("    .ThenBy(s => s.GetType().FullName);");
            AppendLine();
            AppendLine("foreach (var step in steps)");
            Block(() => { AppendLine("step.ConfigurePipeline(app);"); });

            // After the steps, so a policy one of them registers counts as defined.
            if (WiresResilience)
            {
                AppendLine();
                AppendLine("global::Pragmatic.Resilience.Configuration.UndefinedResiliencePolicies.Report(app.ApplicationServices);");
            }
        }, "void", configureParams, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    private void RenderValidateConfigurationMethod()
    {
        AddUsing("Microsoft.Extensions.Configuration");

        XmlSummary("Validates that all required configuration sections are present. Call before building the host.");
        XmlParam("configuration", "The application configuration.");

        var parameters = new List<MethodParameter>
        {
            new("IConfiguration", "configuration")
        };

        Method("ValidateConfiguration", () =>
        {
            AppendLine("var missing = new System.Collections.Generic.List<string>();");

            foreach (var section in _model.RequiredConfigSections)
            {
                var label = string.IsNullOrEmpty(section.SourceModuleType)
                    ? $"\"{section.SectionPath}\""
                    : $"\"{section.SectionPath}\" (required by {section.SourceModuleType})";

                AppendLine($"if (!configuration.GetSection(\"{section.SectionPath}\").Exists())");
                Block(() => AppendLine($"missing.Add({EscapeLiteral(label)});"));
            }

            AppendLine();
            AppendLine("if (missing.Count > 0)");
            Block(() =>
            {
                AppendLine("throw new System.InvalidOperationException(");
                AppendLine("    \"Missing required configuration sections:\\n\" +");
                AppendLine("    string.Join(\"\\n\", missing.Select(m => \"  - \" + m)));");
            });
        }, "void", parameters, AccessModifier.Internal, new MethodModifiers { IsStatic = true });
    }

    /// <summary>
    ///     The distinct <c>Namespace.Class.Method</c> entry points the discovered assemblies publish for
    ///     one metadata category, in a stable order.
    /// </summary>
    /// <remarks>
    ///     Ordered so the generated file does not change when the reference order does, and distinct
    ///     because the same module can be reached twice — a registration called twice registers its
    ///     services twice, and for an additive one that is a wrong number rather than a missing one.
    /// </remarks>
    private List<string> RegistrationMethodsFor(string category)
        => _model.Assemblies
            .SelectMany(a => a.Entries)
            .Where(e => e.Category == category && !string.IsNullOrEmpty(e.RegistrationMethod))
            .Select(e => e.RegistrationMethod)
            .Distinct()
            .OrderBy(m => m, System.StringComparer.Ordinal)
            .ToList();

    /// <summary>
    ///     The same, restricted to the assemblies this host actually hosts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For the categories that register a module's <b>in-process workers</b> — message
    ///         handlers, domain event handlers, jobs, sagas. They run the module's own operations, so
    ///         they belong in the process that owns the module. A host that reaches the module over
    ///         HTTP registers <c>I{Boundary}Actions</c> and nothing else (<c>AddRemote</c>): the
    ///         internal interface those workers reach for cannot be registered there, because it
    ///         publishes the preloaded shapes and a tracked entity does not cross a process.
    ///     </para>
    ///     <para>
    ///         ⚠️ Registering them anyway was not merely a container failure at first use. The same
    ///         handler is subscribed in both hosts, so whichever half could be constructed did the work
    ///         twice (measured on <c>Showcase.Host.Distributed</c>, where Billing is remote
    ///         and its <c>ReservationConfirmedHandler</c> — constructor parameter
    ///         <c>IBillingInternalActions</c> — was registered all the same).
    ///     </para>
    ///     <para>
    ///         The metadata channel categories that are <em>maps</em> rather than workers — redaction,
    ///         JSON contexts, authorization catalog, read contracts, configuration — stay: a host that
    ///         serves a remote module's routes still serialises and redacts its shapes.
    ///     </para>
    /// </remarks>
    private List<string> HostedRegistrationMethodsFor(string category)
    {
        var remote = RemoteAssemblyNames;

        return _model.Assemblies
            .Where(a => !remote.Contains(a.AssemblyName))
            .SelectMany(a => a.Entries)
            .Where(e => e.Category == category && !string.IsNullOrEmpty(e.RegistrationMethod))
            .Select(e => e.RegistrationMethod)
            .Distinct()
            .OrderBy(m => m, System.StringComparer.Ordinal)
            .ToList();
    }
}

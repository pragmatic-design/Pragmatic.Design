using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Validation;

/// <summary>
///     Validates dependency injection registrations at compile time.
/// </summary>
internal static class DependencyValidator
{
    // Types from outside Pragmatic that are always available from DI.
    //
    // Names, so nothing compiles against them: a type that moves leaves its entry matching nothing, and the
    // validator then reports a service the host does register.
    //
    // ⚠️ Nothing of Pragmatic's own belongs here. A list in this file would have to be remembered by
    // whoever adds a Use* extension in another package, nothing would fail when it was not, and the
    // failure would land on the application using that package. A Pragmatic contract the host registers says so itself, with [ProvidedByHost], in
    // the package that registers it. These entries stay because we cannot put an attribute on somebody
    // else's type.
    private static readonly HashSet<string> WellKnownTypes = new(StringComparer.Ordinal)
    {
        // Microsoft.Extensions.DependencyInjection
        "global::Microsoft.Extensions.DependencyInjection.IServiceScopeFactory",
        "global::System.IServiceProvider",

        // Microsoft.Extensions.Logging
        "global::Microsoft.Extensions.Logging.ILogger",
        "global::Microsoft.Extensions.Logging.ILoggerFactory",

        // Microsoft.Extensions.Configuration
        "global::Microsoft.Extensions.Configuration.IConfiguration",

        // Microsoft.Extensions.Hosting
        "global::Microsoft.Extensions.Hosting.IHostEnvironment",
        "global::Microsoft.Extensions.Hosting.IHostApplicationLifetime",

        // ASP.NET Core
        "global::Microsoft.AspNetCore.Hosting.IWebHostEnvironment",
        "global::Microsoft.AspNetCore.Http.IHttpContextAccessor",
        "global::Microsoft.AspNetCore.Http.HttpContext",

        // EF Core
        "global::Microsoft.EntityFrameworkCore.DbContext"
    };

    // Prefixes for generic well-known types. Same rule as above: no Pragmatic name here — an open
    // generic carries the declaration on its definition, and the reader takes the original definition.
    private static readonly string[] WellKnownGenericPrefixes =
    {
        "global::Microsoft.Extensions.Logging.ILogger<",
        "global::Microsoft.Extensions.Options.IOptions<",
        "global::Microsoft.Extensions.Options.IOptionsSnapshot<",
        "global::Microsoft.Extensions.Options.IOptionsMonitor<",
        "global::System.Lazy<",
        "global::System.Func<",
        "global::System.Collections.Generic.IEnumerable<"
    };

    // The subset of the above registered Scoped at runtime. A Singleton capturing one is a captive
    // dependency. Conservative: only types whose Scoped lifetime is unambiguous by framework convention.
    private static readonly HashSet<string> WellKnownScopedTypes = new(StringComparer.Ordinal)
    {
        "global::Microsoft.AspNetCore.Http.HttpContext",
        "global::Microsoft.EntityFrameworkCore.DbContext"
    };

    private static readonly string[] WellKnownScopedGenericPrefixes =
    {
        "global::Microsoft.Extensions.Options.IOptionsSnapshot<"
    };

    private static bool IsWellKnownScopedType(string fullTypeName)
    {
        if (WellKnownScopedTypes.Contains(fullTypeName))
            return true;

        foreach (var prefix in WellKnownScopedGenericPrefixes)
            if (fullTypeName.StartsWith(prefix, StringComparison.Ordinal))
                return true;

        return false;
    }

    /// <summary>
    ///     Validates all services and decorators, reporting diagnostics for issues.
    /// </summary>
    /// <param name="context">Where the diagnostics are reported.</param>
    /// <param name="compilation">Used to turn a stored location back into one the report can point at.</param>
    /// <param name="services">The <c>[Service]</c> classes of this compilation.</param>
    /// <param name="decorators">The <c>[Decorator]</c> classes of this compilation.</param>
    /// <param name="registeredElsewhere">
    ///     The contracts the assemblies this one references say they register — read from their DI
    ///     metadata. Modules composing into one host is the architecture, so a contract another module
    ///     registers is not an edge case; before this was read, <c>IRegistryReads</c> — generated and
    ///     registered by the sibling module — was reported as unregistered, and the application had to
    ///     drop <c>[Service]</c> and register its own class by hand.
    /// </param>
    public static void Validate(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ServiceModel> services,
        ImmutableArray<DecoratorModel> decorators,
        ImmutableArray<RegisteredElsewhere> registeredElsewhere = default)
    {
        // Build lookup of registered service types
        var registeredTypes = BuildRegisteredTypesLookup(services, registeredElsewhere);
        var lifetimeElsewhere = BuildLifetimeLookup(registeredElsewhere);

        // Validate each service
        foreach (var service in services)
            ValidateServiceDependencies(context, compilation, service, registeredTypes, services, lifetimeElsewhere);

        // Detect circular dependencies
        DetectCircularDependencies(context, compilation, services);

        // Validate decorators
        foreach (var decorator in decorators)
            ValidateDecorator(context, compilation, decorator, registeredTypes);
    }

    private static HashSet<string> BuildRegisteredTypesLookup(
        ImmutableArray<ServiceModel> services,
        ImmutableArray<RegisteredElsewhere> registeredElsewhere)
    {
        var lookup = new HashSet<string>(StringComparer.Ordinal);

        if (!registeredElsewhere.IsDefaultOrEmpty)
            foreach (var registration in registeredElsewhere)
                lookup.Add(registration.Contract);

        foreach (var service in services)
        {
            // Add the service type (interface or self)
            lookup.Add(service.ServiceTypeName);

            // Only add the implementation type if AsSelf is true
            // Otherwise, the concrete type is NOT directly resolvable from DI
            if (service.AsSelf)
                lookup.Add(service.FullTypeName);
        }

        return lookup;
    }

    /// <summary>
    ///     The lifetime each contract registered in another assembly is registered with, so a singleton
    ///     of this assembly capturing a per-request one is still reported: the cross-assembly half must
    ///     not buy silence on both counts at once.
    /// </summary>
    private static Dictionary<string, string> BuildLifetimeLookup(
        ImmutableArray<RegisteredElsewhere> registeredElsewhere)
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);

        if (registeredElsewhere.IsDefaultOrEmpty)
            return lookup;

        foreach (var registration in registeredElsewhere)
            if (registration.Lifetime is { Length: > 0 })
                lookup[registration.Contract] = registration.Lifetime;

        return lookup;
    }

    private static void ValidateServiceDependencies(
        SourceProductionContext context,
        Compilation compilation,
        ServiceModel service,
        HashSet<string> registeredTypes,
        ImmutableArray<ServiceModel> allServices,
        Dictionary<string, string> lifetimeElsewhere)
    {
        // Validate constructor dependencies
        foreach (var dependency in service.Dependencies)
            ValidateDependency(context, compilation, service, dependency, registeredTypes, allServices,
                lifetimeElsewhere);

        // Validate property injection dependencies
        foreach (var prop in service.PropertyInjections)
        {
            // Convert to DependencyModel for validation
            // For display only: strip global::, take the outermost simple name before any '<'.
            var displayName = prop.PropertyTypeName.Replace("global::", string.Empty);
            var genericBracket = displayName.IndexOf('<');
            if (genericBracket >= 0)
                displayName = displayName.Substring(0, genericBracket);
            var lastDot = displayName.LastIndexOf('.');
            if (lastDot >= 0)
                displayName = displayName.Substring(lastDot + 1);

            var dependency = new DependencyModel
            {
                TypeName = displayName,
                FullTypeName = prop.PropertyTypeName,
                IsOptional = !prop.IsRequired,
                Key = prop.Key,
                IsProvidedByHost = prop.IsProvidedByHost,
                ProvidedByHostLifetime = prop.ProvidedByHostLifetime
            };
            ValidateDependency(context, compilation, service, dependency, registeredTypes, allServices,
                lifetimeElsewhere);
        }

        // Validate method injection dependencies
        foreach (var method in service.MethodInjections)
            foreach (var param in method.Parameters)
                ValidateDependency(context, compilation, service, param, registeredTypes, allServices,
                    lifetimeElsewhere);
    }

    private static void ValidateDependency(
        SourceProductionContext context,
        Compilation compilation,
        ServiceModel service,
        DependencyModel dependency,
        HashSet<string> registeredTypes,
        ImmutableArray<ServiceModel> allServices,
        Dictionary<string, string> lifetimeElsewhere)
    {
        // Skip optional dependencies
        if (dependency.IsOptional)
            return;

        // The contract says the host registers it ([ProvidedByHost]): this compilation cannot see that
        // registration. What it CAN see is the lifetime the contract names, and a singleton that takes a
        // per-request one is the same captive dependency as below — a declaration that bought silence on
        // both counts is what the well-known list did, and what this stopped doing.
        if (dependency.IsProvidedByHost)
        {
            ReportIfCaptive(context, compilation, service, dependency, dependency.ProvidedByHostLifetime);
            return;
        }

        // Skip well-known framework types — EXCEPT a Singleton capturing a well-known SCOPED service
        // (DbContext, HttpContext, IOptionsSnapshot<>). The blanket early return hid the single most
        // common captive-dependency bug (a singleton holding a per-request service → stale/cross-request
        // state), because well-known types are never in registeredTypes and so never reached the
        // lifetime check below.
        if (IsWellKnownType(dependency.FullTypeName))
        {
            if (IsWellKnownScopedType(dependency.FullTypeName))
                ReportIfCaptive(context, compilation, service, dependency, "Scoped");
            return;
        }

        // Check if dependency is registered
        if (!registeredTypes.Contains(dependency.FullTypeName))
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DependencyNotRegistered,
                service.LocationInfo?.ToLocation(compilation),
                service.TypeName,
                dependency.TypeName));
        else if (lifetimeElsewhere.TryGetValue(dependency.FullTypeName, out var lifetime))
            // Registered by another assembly, which said with which lifetime.
            ReportIfCaptive(context, compilation, service, dependency, lifetime);
        else
            // Validate lifetime compatibility
            ValidateLifetimeCompatibility(context, compilation, service, dependency, allServices);
    }

    /// <summary>
    ///     Reports PRAG1642 when a singleton takes a dependency that lives shorter than it does: one
    ///     instance would hold the first scope's for every scope after it.
    /// </summary>
    private static void ReportIfCaptive(
        SourceProductionContext context,
        Compilation compilation,
        ServiceModel service,
        DependencyModel dependency,
        string? dependencyLifetime)
    {
        if (service.Lifetime != "Singleton" || dependencyLifetime is not ("Scoped" or "Transient"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            CompositionDiagnostics.LifetimeMismatch,
            service.LocationInfo?.ToLocation(compilation),
            service.TypeName,
            dependencyLifetime,
            dependency.TypeName));
    }

    private static void ValidateLifetimeCompatibility(
        SourceProductionContext context,
        Compilation compilation,
        ServiceModel service,
        DependencyModel dependency,
        ImmutableArray<ServiceModel> allServices)
    {
        // Only check if the service is Singleton
        if (service.Lifetime != "Singleton")
            return;

        // Find the dependency's registration
        var dependencyService = allServices.FirstOrDefault(s =>
            s.ServiceTypeName == dependency.FullTypeName ||
            s.FullTypeName == dependency.FullTypeName);

        if (dependencyService is null)
            return;

        // Singleton cannot depend on Scoped or Transient
        if (dependencyService.Lifetime is "Scoped" or "Transient")
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.LifetimeMismatch,
                service.LocationInfo?.ToLocation(compilation),
                service.TypeName,
                dependencyService.Lifetime,
                dependency.TypeName));
    }

    private static void DetectCircularDependencies(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ServiceModel> services)
    {
        // Build adjacency list for dependency graph
        var graph = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var serviceByType = new Dictionary<string, ServiceModel>(StringComparer.Ordinal);

        foreach (var service in services)
        {
            var key = service.ServiceTypeName;
            serviceByType[key] = service;

            if (!graph.ContainsKey(key))
                graph[key] = new List<string>();

            foreach (var dep in service.Dependencies)
                if (!dep.IsOptional)
                    graph[key].Add(dep.FullTypeName);
        }

        // DFS to detect cycles
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var recursionStack = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var service in services)
        {
            var key = service.ServiceTypeName;
            if (!visited.Contains(key))
                if (DetectCycleDfs(key, graph, visited, recursionStack, path, out var cycle))
                {
                    // Report the cycle
                    var cycleDescription = string.Join(" -> ", cycle);

                    // Find the service that starts the cycle
                    var cycleStartType = cycle.FirstOrDefault();
                    if (cycleStartType is not null && serviceByType.TryGetValue(cycleStartType, out var cycleService))
                        context.ReportDiagnostic(Diagnostic.Create(
                            CompositionDiagnostics.CircularDependency,
                            cycleService.LocationInfo?.ToLocation(compilation),
                            cycleDescription));

                    // Only report one cycle to avoid noise
                    return;
                }
        }
    }

    private static bool DetectCycleDfs(
        string current,
        Dictionary<string, List<string>> graph,
        HashSet<string> visited,
        HashSet<string> recursionStack,
        List<string> path,
        out List<string> cycle)
    {
        cycle = new List<string>();

        visited.Add(current);
        recursionStack.Add(current);
        path.Add(current);

        if (graph.TryGetValue(current, out var neighbors))
            foreach (var neighbor in neighbors)
                if (!visited.Contains(neighbor))
                {
                    if (DetectCycleDfs(neighbor, graph, visited, recursionStack, path, out cycle))
                        return true;
                }
                else if (recursionStack.Contains(neighbor))
                {
                    // Found a cycle - extract it from path
                    var cycleStart = path.IndexOf(neighbor);
                    if (cycleStart >= 0)
                    {
                        cycle = path.Skip(cycleStart).ToList();
                        cycle.Add(neighbor); // Complete the cycle
                    }

                    return true;
                }

        path.RemoveAt(path.Count - 1);
        recursionStack.Remove(current);
        return false;
    }

    private static void ValidateDecorator(
        SourceProductionContext context,
        Compilation compilation,
        DecoratorModel decorator,
        HashSet<string> registeredTypes)
    {
        // Check if decorator has inner service parameter
        if (!decorator.HasInnerServiceParameter)
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DecoratorMissingInnerService,
                decorator.LocationInfo?.ToLocation(compilation),
                decorator.TypeName));

        // Check if decorated interface has a registration
        if (!registeredTypes.Contains(decorator.DecoratedInterface))
            context.ReportDiagnostic(Diagnostic.Create(
                CompositionDiagnostics.DependencyNotRegistered,
                decorator.LocationInfo?.ToLocation(compilation),
                decorator.TypeName,
                decorator.DecoratedInterface.Replace("global::", "")));
    }

    private static bool IsWellKnownType(string fullTypeName)
    {
        // Check exact matches
        if (WellKnownTypes.Contains(fullTypeName))
            return true;

        // Check generic prefixes
        foreach (var prefix in WellKnownGenericPrefixes)
            if (fullTypeName.StartsWith(prefix, StringComparison.Ordinal))
                return true;

        return false;
    }
}

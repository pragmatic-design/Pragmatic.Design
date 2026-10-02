// Pragmatic.SourceGenerator - Composition - Module Model

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Composition.Models;

/// <summary>
///     Represents a discovered [Module] definition.
/// </summary>
internal sealed record ModuleModel
{
    /// <summary>
    ///     The module name (from attribute or assembly name).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     The full type name of the class with [Module] attribute.
    /// </summary>
    public required string FullTypeName { get; init; }

    /// <summary>
    ///     The namespace of the module class.
    /// </summary>
    public required string Namespace { get; init; }

    /// <summary>
    ///     Module dependencies (other module names).
    /// </summary>
    public EquatableArray<string> DependsOn { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Module version (optional).
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    ///     Module description (optional).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Location for diagnostics.
    /// </summary>
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>
    ///     Assembly name where this module is defined.
    /// </summary>
    public string? AssemblyName { get; init; }

    /// <summary>
    ///     [Include&lt;T&gt;] declarations on this module (host-level wiring to databases and DbContexts).
    ///     Only populated for host [Module] classes.
    /// </summary>
    public EquatableArray<HostIncludeModel> HostIncludes { get; init; } = EquatableArray<HostIncludeModel>.Empty;

    /// <summary>
    ///     Fully qualified type names of startup steps declared via [NeedsStep&lt;T&gt;].
    /// </summary>
    public EquatableArray<string> NeedsSteps { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     [UsePackage&lt;T&gt;] declarations on this module.
    ///     Package metadata are fused into the module's metadata at compile-time.
    /// </summary>
    public EquatableArray<UsePackageModel> UsePackages { get; init; } = EquatableArray<UsePackageModel>.Empty;

    /// <summary>
    ///     If this assembly IS a package (has IPackageDefinition), its route prefix.
    ///     Embedded in module metadata so the host can group package endpoints.
    /// </summary>
    public string? PackageRoutePrefix { get; init; }

    /// <summary>
    ///     [ExposeEndpoint&lt;T&gt;] declarations on this module.
    ///     The host generates endpoint handlers for these actions.
    /// </summary>
    public EquatableArray<ExposedEndpointModel> ExposedEndpoints { get; init; } = EquatableArray<ExposedEndpointModel>.Empty;

    /// <summary>
    ///     [RemoteBoundary&lt;T&gt;] declarations on this module.
    ///     Actions from these modules are invoked via HTTP instead of in-process.
    /// </summary>
    public EquatableArray<RemoteBoundaryModel> RemoteBoundaries { get; init; } = EquatableArray<RemoteBoundaryModel>.Empty;

    /// <summary>
    ///     True when the class carries <c>[AnonymousHost]</c>: the host deliberately has no
    ///     authentication, so its endpoint root does not require authorization and PRAG1695 is not
    ///     reported. Meaningful on a host [Module] class only.
    /// </summary>
    public bool IsAnonymousHost { get; init; }
}

/// <summary>
///     Aggregated module information from referenced assemblies (for HOST mode).
/// </summary>
internal sealed record DiscoveredModuleInfo
{
    /// <summary>
    ///     Module name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Assembly where the module is defined.
    /// </summary>
    public required string AssemblyName { get; init; }

    /// <summary>
    ///     Module dependencies.
    /// </summary>
    public EquatableArray<string> DependsOn { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Module version.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    ///     Module description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    ///     Registration method to call for this module's DI services.
    /// </summary>
    public string? DiRegistrationMethod { get; init; }

    /// <summary>
    ///     Registration method to call for this module's startups.
    /// </summary>
    public string? StartupRegistrationMethod { get; init; }

    /// <summary>
    ///     Fully qualified boundary type name (with global:: prefix).
    ///     Used by DatabaseTopologyValidator to correlate modules with [Include&lt;T&gt;] declarations.
    /// </summary>
    public string? BoundaryTypeName { get; init; }

    /// <summary>
    ///     Fully qualified entity type names declared with [ReadAccess&lt;T&gt;] on this boundary.
    ///     These entities belong to other boundaries but are accessed via SQL join.
    /// </summary>
    public EquatableArray<string> ReadAccessTypes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The name of the method that registers this module's DbContext: <c>Add{ModuleName}DbContext</c>.
    ///     Derived by convention from <see cref="BoundaryTypeName" />; null when Persistence.EFCore.SG is not in use.
    ///     The class that declares it is the persistence generator's to name
    ///     (<c>PersistedStoresModel.RegistrationClass</c>), and the host calls it through that class.
    /// </summary>
    public string? DbContextRegistrationMethod { get; init; }

    /// <summary>
    ///     Assembly names of packages imported via [UsePackage&lt;T&gt;] on this module.
    ///     The host SG fuses package metadata (actions, services) into the module's registration.
    /// </summary>
    public EquatableArray<string> PackageAssemblyNames { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Route prefixes for packages, keyed by assembly name.
    ///     Used to group package endpoints under MapGroup(routePrefix).
    /// </summary>
    public EquatableArray<PackageRoutePrefixInfo> PackageRoutePrefixes { get; init; } =
        EquatableArray<PackageRoutePrefixInfo>.Empty;

    /// <summary>
    ///     If this assembly IS a package, its own route prefix from IPackageDefinition.
    /// </summary>
    public string? PackageRoutePrefix { get; init; }

    /// <summary>
    ///     [ExposeEndpoint&lt;T&gt;] declarations discovered from module metadata JSON.
    ///     The host generates endpoint handlers for these actions.
    /// </summary>
    public EquatableArray<ExposedEndpointModel> ExposedEndpoints { get; init; } = EquatableArray<ExposedEndpointModel>.Empty;
}

/// <summary>
///     Maps a package assembly to its endpoint route prefix.
/// </summary>
internal sealed record PackageRoutePrefixInfo
{
    public required string AssemblyName { get; init; }
    public string? RoutePrefix { get; init; }
}


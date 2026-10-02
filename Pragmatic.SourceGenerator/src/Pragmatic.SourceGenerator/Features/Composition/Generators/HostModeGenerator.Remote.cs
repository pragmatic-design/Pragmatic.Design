using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Composition.Diagnostics;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Composition.Templates;
using Pragmatic.SourceGenerator.Features.Composition.Transforms;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

/// <summary>
///     Remote boundary support in the host: which actions stay local, and how packages are resolved.
/// </summary>
/// <remarks>
///     The host does NOT generate a per-action HTTP invoker. A remote boundary is served by the
///     <c>{Module}RemoteActions</c> class the module itself emits
///     (<c>BoundaryInterfaceTemplate.Remote</c>), which the host activates by calling
///     <c>Add{Module}Boundary(services, BoundaryMode.Remote)</c>. Anything about how a remote call is
///     made — including streaming a <c>FileResponse</c> instead of deserializing it — belongs there.
/// </remarks>
internal static partial class HostModeGenerator
{
    /// <summary>
    ///     Returns actions that are NOT from remote boundary assemblies (i.e., local actions).
    /// </summary>
    private static ImmutableArray<DiscoveredActionInfo> GetNonRemoteActions(
        ImmutableArray<DiscoveredActionInfo> allActions,
        ImmutableArray<RemoteBoundaryModel> remoteBoundaries)
    {
        if (remoteBoundaries.IsDefaultOrEmpty)
            return allActions;

        var remoteAssemblies = new HashSet<string>(
            remoteBoundaries
                .Where(rb => rb.AssemblyName is not null)
                .Select(rb => rb.AssemblyName!),
            StringComparer.Ordinal);

        return allActions
            .Where(a => !remoteAssemblies.Contains(a.SourceAssembly))
            .ToImmutableArray();
    }

    /// <summary>
    ///     Builds a map from package assembly name to route prefix.
    ///     Sources: UsePackage override → consumer module metadata → package's own metadata.
    /// </summary>
    private static Dictionary<string, string?> BuildPackageRoutePrefixMap(
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> discoveredModules)
    {
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);

        // Priority 3: From package's own module metadata (packageRoutePrefix field)
        if (!discoveredModules.IsDefaultOrEmpty)
        {
            foreach (var module in discoveredModules)
            {
                if (module.PackageRoutePrefix is not null && !string.IsNullOrEmpty(module.AssemblyName))
                    map[module.AssemblyName] = module.PackageRoutePrefix;
            }
        }

        // Priority 2: From consumer modules' metadata (UsePackage route prefixes)
        if (!discoveredModules.IsDefaultOrEmpty)
        {
            foreach (var module in discoveredModules)
            {
                if (module.PackageRoutePrefixes.IsDefaultOrEmpty) continue;
                foreach (var prf in module.PackageRoutePrefixes)
                {
                    if (!string.IsNullOrEmpty(prf.AssemblyName) && prf.RoutePrefix is not null)
                        map[prf.AssemblyName] = prf.RoutePrefix;
                }
            }
        }

        // Priority 1 (highest): From local UsePackage attributes (compile-time override)
        if (!localModules.IsDefaultOrEmpty)
        {
            foreach (var module in localModules)
            {
                if (module.UsePackages.IsDefaultOrEmpty) continue;
                foreach (var pkg in module.UsePackages)
                {
                    if (!string.IsNullOrEmpty(pkg.PackageAssemblyName) && pkg.RoutePrefix is not null)
                        map[pkg.PackageAssemblyName] = pkg.RoutePrefix;
                }
            }
        }

        return map;
    }

    /// <summary>
    ///     The package assemblies this host imports, through the modules it hosts: the
    ///     <c>[UsePackage&lt;T&gt;]</c> declarations of local modules (compile-time attributes) and of
    ///     discovered ones (metadata JSON), with no duplicate package type per local module.
    /// </summary>
    /// <param name="context">Where the duplicate-import diagnostic is reported.</param>
    /// <param name="localModules">Modules declared in the host's own assembly.</param>
    /// <param name="discoveredModules">Modules read from the referenced assemblies' metadata.</param>
    /// <param name="includedModuleAssemblies">
    ///     The assemblies of the modules this host actually hosts, or <c>null</c> when it declares no
    ///     topology at all and therefore hosts everything it can see.
    /// </param>
    /// <remarks>
    ///     ⚠️ A package is imported by a <b>module</b>, with <c>[UsePackage&lt;TPackage, TBoundary&gt;]</c>,
    ///     and it brings that package's actions, stores and endpoints. So a package reaches a host only
    ///     through a module the host hosts, never through <b>any</b> module in the compilation.
    ///     <para>
    ///         Unfiltered, a host that hosts one module, whose references reach a module importing
    ///         <c>Authorization.Management</c>, would register that package's invokers and not its
    ///         stores: <c>builder.Build()</c> throws with "Unable to resolve service for type
    ///         'IRolePermissionStore'" before any request, and the host answers nothing at all because
    ///         a module it does not host has a package.
    ///     </para>
    ///     <para>
    ///         The local branch below is deliberately unfiltered: a local module is declared in the
    ///         host's own assembly, so the host hosts it by construction.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<string> CollectAndValidatePackages(
        SourceProductionContext context,
        ImmutableArray<ModuleModel> localModules,
        ImmutableArray<DiscoveredModuleInfo> discoveredModules,
        HashSet<string>? includedModuleAssemblies)
    {
        var packageAssemblyNames = ImmutableArray.CreateBuilder<string>();

        // From local modules (compile-time [UsePackage<T>] attributes)
        if (!localModules.IsDefaultOrEmpty)
        {
            foreach (var module in localModules)
            {
                if (module.UsePackages.IsDefaultOrEmpty)
                    continue;

                var seenPackages = new HashSet<string>(StringComparer.Ordinal);
                foreach (var pkg in module.UsePackages)
                {
                    if (!seenPackages.Add(pkg.PackageTypeName))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            CompositionDiagnostics.DuplicateUsePackage,
                            pkg.Location ?? Location.None,
                            pkg.PackageTypeName,
                            module.Name));
                        continue;
                    }

                    if (!string.IsNullOrEmpty(pkg.PackageAssemblyName) &&
                        !packageAssemblyNames.Contains(pkg.PackageAssemblyName))
                        packageAssemblyNames.Add(pkg.PackageAssemblyName);
                }
            }
        }

        // From discovered modules (metadata JSON "packages" field)
        if (!discoveredModules.IsDefaultOrEmpty)
        {
            foreach (var module in discoveredModules)
            {
                if (module.PackageAssemblyNames.IsDefaultOrEmpty)
                    continue;

                // The module is in the compilation but this host does not host it, so neither does it
                // host what that module imports.
                if (includedModuleAssemblies is not null
                    && (string.IsNullOrEmpty(module.AssemblyName)
                        || !includedModuleAssemblies.Contains(module.AssemblyName!)))
                {
                    continue;
                }

                foreach (var pkgAssembly in module.PackageAssemblyNames)
                {
                    if (!string.IsNullOrEmpty(pkgAssembly) &&
                        !packageAssemblyNames.Contains(pkgAssembly))
                        packageAssemblyNames.Add(pkgAssembly);
                }
            }
        }

        return packageAssemblyNames.ToImmutable();
    }
}

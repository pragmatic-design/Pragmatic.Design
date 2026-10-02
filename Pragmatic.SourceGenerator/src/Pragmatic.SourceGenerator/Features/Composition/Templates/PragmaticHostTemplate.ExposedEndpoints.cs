// Pragmatic.SourceGenerator - Composition - Pragmatic Host Template (Exposed endpoints)

using Pragmatic.SourceGenerator.Features.Composition.Models;

namespace Pragmatic.SourceGenerator.Features.Composition.Templates;

/// <summary>
///     Mapping of the handlers <c>[ExposeEndpoint&lt;TAction&gt;]</c> and
///     <c>[ExposeEndpoint&lt;TAction, TGroup&gt;]</c> produce.
/// </summary>
internal sealed partial class PragmaticHostTemplate
{
    /// <summary>
    ///     Maps each exposed endpoint on its group when it names one, otherwise under the package's
    ///     route prefix, otherwise on the root.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The group has to be read here, not only into the model: otherwise a route declared "relative
    ///     to the group's route prefix" is published outside the group, without its prefix and without
    ///     any option <c>ConfigureGroup</c> puts on it. The group's <c>MapGroup</c> variable exists because
    ///     <c>HostModeGenerator.AddExposedEndpointGroups</c> put the group among the discovered ones.
    /// </remarks>
    private void RenderExposedEndpointMappings(HashSet<string> emittedPackageVars)
    {
        var groupTypes = new HashSet<string>(
            _model.DiscoveredEndpointGroups.IsDefaultOrEmpty
                ? []
                : _model.DiscoveredEndpointGroups.Select(g => g.GroupType),
            StringComparer.Ordinal);

        var grouped = _model.ExposedEndpoints
            .Where(e => e.GroupTypeName is not null && groupTypes.Contains(e.GroupTypeName))
            .OrderBy(e => e.ActionSimpleName)
            .ToList();

        foreach (var ep in grouped)
        {
            Comment($"Exposed endpoint in {GetSimpleName(ep.GroupTypeName!)}");
            AppendLine($"{_model.RootNamespace}.Endpoints.{ep.ActionSimpleName}EndpointHandler.MapEndpoint({GetGroupVariableName(ep.GroupTypeName!)});");
        }

        if (grouped.Count > 0)
            AppendLine();

        // A group the host could not resolve is PRAG1682, an error: the route is not mapped anywhere
        // rather than on the root, so the build stops on the diagnostic instead of on a second error.
        var exposedByAssembly = _model.ExposedEndpoints
            .Where(e => e.GroupTypeName is null)
            .GroupBy(e => e.ActionAssemblyName)
            .OrderBy(g => g.Key);

        foreach (var asmGroup in exposedByAssembly)
        {
            var routePrefix = ResolveExposedRoutePrefix(asmGroup.Key);
            var target = "root";

            if (routePrefix is not null)
            {
                target = GetPackageGroupVariableName(routePrefix);
                // Emit group var if not already emitted by package endpoints above
                if (emittedPackageVars.Add(target))
                {
                    Comment($"Package: {routePrefix}");
                    AppendLine($"var {target} = root.MapGroup(\"{routePrefix}\");");
                    AppendLine();
                }
            }

            Comment($"Exposed endpoints from {asmGroup.Key}");
            foreach (var ep in asmGroup.OrderBy(e => e.ActionSimpleName))
                AppendLine($"{_model.RootNamespace}.Endpoints.{ep.ActionSimpleName}EndpointHandler.MapEndpoint({target});");
            AppendLine();
        }
    }

    /// <summary>
    ///     The route prefix of the package an exposed action comes from: the one a local module's
    ///     <c>[UsePackage]</c> gives it, else the package's own.
    /// </summary>
    private string? ResolveExposedRoutePrefix(string actionAssemblyName)
    {
        // The last local [UsePackage] naming the assembly wins, as it always has.
        string? fromUsePackage = null;
        foreach (var module in _model.LocalModules)
        {
            if (module.UsePackages.IsDefaultOrEmpty) continue;
            foreach (var pkg in module.UsePackages)
            {
                if (pkg.PackageAssemblyName == actionAssemblyName && pkg.RoutePrefix is not null)
                    fromUsePackage = pkg.RoutePrefix;
            }
        }

        if (fromUsePackage is not null)
            return fromUsePackage;

        foreach (var module in _model.DiscoveredModules)
        {
            if (module.AssemblyName == actionAssemblyName && module.PackageRoutePrefix is not null)
                return module.PackageRoutePrefix;

            if (module.PackageRoutePrefixes.IsDefaultOrEmpty) continue;
            foreach (var prf in module.PackageRoutePrefixes)
            {
                if (prf.AssemblyName == actionAssemblyName && prf.RoutePrefix is not null)
                    return prf.RoutePrefix;
            }
        }

        return null;
    }
}

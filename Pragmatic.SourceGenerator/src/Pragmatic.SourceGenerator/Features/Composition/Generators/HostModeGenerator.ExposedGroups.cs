using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Composition.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Templates;
using Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

namespace Pragmatic.SourceGenerator.Features.Composition.Generators;

internal static partial class HostModeGenerator
{
    /// <summary>
    ///     Adds to the host's groups every group an <c>[ExposeEndpoint&lt;TAction, TGroup&gt;]</c> names and
    ///     no endpoint metadata listed, with its parents.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A library's endpoint metadata lists the groups its own <c>[Endpoint]</c>s use and nothing
    ///         else. A group named only by an exposed endpoint — the ordinary case, an application
    ///         publishing a package action under a group of its own — is in no list, so without this the host
    ///         has no <c>MapGroup</c> to put the route in.
    ///     </para>
    ///     <para>
    ///         The host sees every referenced assembly, so the group is read here from its
    ///         symbol, with the same parser the endpoint transform uses. A name that resolves to nothing,
    ///         or to a type that is not an <c>[EndpointGroup]</c>, is PRAG1682: falling back to the root
    ///         would publish the route outside the group's path and options, which is the defect.
    ///     </para>
    /// </remarks>
    private static ImmutableArray<DiscoveredEndpointGroupInfo> AddExposedEndpointGroups(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<ExposedEndpointModel> exposedEndpoints,
        ImmutableArray<DiscoveredEndpointGroupInfo> discoveredGroups)
    {
        if (exposedEndpoints.IsDefaultOrEmpty)
            return discoveredGroups;

        var known = new HashSet<string>(
            discoveredGroups.IsDefaultOrEmpty ? [] : discoveredGroups.Select(g => g.GroupType),
            StringComparer.Ordinal);
        var added = ImmutableArray.CreateBuilder<DiscoveredEndpointGroupInfo>();

        foreach (var ep in exposedEndpoints)
        {
            if (ep.GroupTypeName is null || known.Contains(ep.GroupTypeName))
                continue;

            var model = EndpointTransform.ParseGroup(ResolveGroupSymbol(compilation, ep.GroupTypeName));
            var unusable = model;
            while (unusable is { IsUnusable: false })
                unusable = unusable.Parent;

            var problem = (model, unusable) switch
            {
                (null, _) => "cannot be resolved from the host",
                (_, { NotFound: true }) => "cannot be resolved from the host (or its parent group cannot)",
                (_, { TypeExistsButNotGroup: true }) => "is not declared with [EndpointGroup(\"prefix\")] (or its parent group is not)",
                _ => null
            };

            if (problem is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.CompositionDiagnostics.ExposedEndpointGroupNotFound, Location.None,
                    ep.ActionSimpleName, SimpleNameOf(ep.GroupTypeName), ep.HostBoundaryName, problem));
                continue;
            }

            // The group and every parent the metadata did not already carry: a child's MapGroup hangs
            // from its parent's variable.
            for (var group = model; group is not null && !known.Contains(Qualified(group.TypeName)); group = group.Parent)
            {
                known.Add(Qualified(group.TypeName));
                added.Add(new DiscoveredEndpointGroupInfo
                {
                    GroupType = Qualified(group.TypeName),
                    RoutePrefix = EndpointMetadataTemplate.GetFullRoutePrefix(group),
                    ParentGroupType = group.Parent is null ? null : Qualified(group.Parent.TypeName),
                    SourceAssembly = compilation.AssemblyName ?? string.Empty
                });
            }
        }

        if (added.Count == 0)
            return discoveredGroups;

        return discoveredGroups.IsDefaultOrEmpty
            ? added.ToImmutable()
            : discoveredGroups.AddRange(added);
    }

    /// <summary>
    ///     The group's symbol from its fully qualified display name, trying the nested-type spellings
    ///     metadata names use (<c>Outer+Inner</c>) when the plain one finds nothing.
    /// </summary>
    private static INamedTypeSymbol? ResolveGroupSymbol(Compilation compilation, string displayName)
    {
        var candidate = displayName.StartsWith("global::", StringComparison.Ordinal)
            ? displayName.Substring("global::".Length)
            : displayName;

        while (true)
        {
            if (compilation.GetTypeByMetadataName(candidate) is { } symbol)
                return symbol;

            // Outer.Inner → Outer+Inner, one level at a time from the right.
            var dot = candidate.LastIndexOf('.');
            if (dot <= 0)
                return null;

            candidate = candidate.Substring(0, dot) + "+" + candidate.Substring(dot + 1);
        }
    }

    /// <summary>The metadata writes group types with <c>global::</c>; the parser does too, but say it once.</summary>
    private static string Qualified(string typeName)
        => typeName.StartsWith("global::", StringComparison.Ordinal) ? typeName : "global::" + typeName;

    private static string SimpleNameOf(string typeName)
    {
        var dot = typeName.LastIndexOf('.');
        return dot < 0 ? typeName : typeName.Substring(dot + 1);
    }
}

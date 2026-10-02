using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Diagnostics;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     Resolves <c>[RequirePermission(GeneratedConst)]</c> references on endpoints against the
///     permission catalog. The transform cannot bind a constant this generator emits in the same
///     compilation, so it keeps the path as written; here — once every producer has contributed — the
///     path becomes the permission value the handler is configured with. Without this the endpoint was
///     mapped with no authorization at all, and the build stayed green.
/// </summary>
internal static partial class EndpointsFeature
{
    internal static EndpointModel ResolveEndpointPermissions(
        EndpointModel endpoint, EquatableArray<PermissionConstEntry> catalog)
    {
        var auth = endpoint.Authorization;
        if (auth is null || !auth.HasUnresolvedPermissionPaths)
            return endpoint;

        var index = PermissionCatalogLookup.Index(catalog);

        return endpoint with
        {
            Authorization = auth with
            {
                RequiredPermissions = MergeResolved(
                    auth.RequiredPermissions, auth.UnresolvedRequiredPermissionPaths, index),
                AnyPermissions = MergeResolved(
                    auth.AnyPermissions, auth.UnresolvedAnyPermissionPaths, index)
            }
        };
    }

    private static EquatableArray<string> MergeResolved(
        EquatableArray<string> resolved, EquatableArray<string> unresolvedPaths, Dictionary<string, string> index)
    {
        if (unresolvedPaths.IsDefaultOrEmpty)
            return resolved;

        var builder = ImmutableArray.CreateBuilder<string>();
        builder.AddRange(resolved.AsImmutableArray());
        foreach (var path in unresolvedPaths)
        {
            var value = PermissionCatalogLookup.Resolve(path, index);
            if (value is not null && !builder.Contains(value))
                builder.Add(value);
        }

        return new EquatableArray<string>(builder.ToImmutable());
    }

    /// <summary>
    ///     Reports PRAG0528 for every constant path the catalog could not resolve — that permission is
    ///     not enforced, so the endpoint is open to any authenticated caller.
    /// </summary>
    private static void ReportUnresolvedEndpointPermissions(
        SourceProductionContext context,
        (EndpointModel Endpoint, EquatableArray<PermissionConstEntry> Catalog) input)
    {
        var auth = input.Endpoint.Authorization;
        if (auth is null || !auth.HasUnresolvedPermissionPaths)
            return;

        var index = PermissionCatalogLookup.Index(input.Catalog);
        ReportUnresolved(context, input.Endpoint, auth.UnresolvedRequiredPermissionPaths, index);
        ReportUnresolved(context, input.Endpoint, auth.UnresolvedAnyPermissionPaths, index);
    }

    private static void ReportUnresolved(
        SourceProductionContext context, EndpointModel endpoint,
        EquatableArray<string> paths, Dictionary<string, string> index)
    {
        foreach (var path in paths)
        {
            if (PermissionCatalogLookup.Resolve(path, index) is null)
                context.ReportDiagnostic(Diagnostic.Create(
                    EndpointsDiagnostics.PermissionConstNotResolved,
                    endpoint.Location, endpoint.TypeName, path));
        }
    }
}

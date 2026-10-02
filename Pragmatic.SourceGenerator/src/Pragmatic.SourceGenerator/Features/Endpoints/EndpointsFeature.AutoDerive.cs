using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions;
using Pragmatic.SourceGenerator.Features.Actions.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     The auto-derivation posture, applied to a <c>[Query]</c>.
/// </summary>
/// <remarks>
///     <para>
///         The route is configured from <see cref="EndpointModel.Authorization" />, so the derivation
///         has to land on the endpoint model — the same rule
///         <see cref="ActionsFeature.ResolvePermission" /> applies to an action, asked with the same
///         inputs, writing into the field the handler template reads.
///     </para>
///     <para>
///         ⚠️ <b>And on the query model too</b>, in <c>QueryFeature.DerivePermission</c>: a query has
///         an invoker, which the boundary facade builds, so landing on the route only would leave a
///         read that declared nothing refused over HTTP and served in process. The two call sites ask
///         <b>one</b> function with the same inputs — two readings of "does this opt out" is how two
///         doors come apart.
///     </para>
///     <para>
///         ⚠️ Without this the posture would run over actions and mutations only. A query without
///         <c>[RequirePermission]</c> and without <c>[AllowAnonymous]</c> would be mapped with no
///         requirement at all — the bare <c>RequireAuthorization()</c> of the root group, open to every
///         authenticated caller. This is the one posture that exists so nothing fails open, and reads
///         are where it would fail open. Invisible, because a route with no requirement answers 200,
///         which is what a correctly authorised call also looks like.
///     </para>
/// </remarks>
internal static partial class EndpointsFeature
{
    /// <summary>
    ///     The query endpoint with its derived permission, or the endpoint untouched — the same array
    ///     element — when the switch is off, the endpoint is not a <c>[Query]</c>, or the query already
    ///     says what it requires.
    /// </summary>
    internal static EndpointModel DeriveQueryPermission(EndpointModel endpoint, PermissionDerivationInputs inputs)
    {
        // An action or a mutation behind an endpoint derives on its own model, and is enforced there.
        if (!inputs.Enabled || !endpoint.IsQuery || endpoint.IsDomainAction || endpoint.IsMutation)
            return endpoint;

        var auth = endpoint.Authorization;
        var resolved = ActionsFeature.ResolvePermission(
            endpoint.IsValid,
            allowAnonymous: auth?.AllowAnonymous == true,
            hasPermissionRequirement: auth is not null && (auth.IsRequired || !auth.RequiredPermissions.IsDefaultOrEmpty
                                                           || !auth.AnyPermissions.IsDefaultOrEmpty),
            hasUnresolvedPaths: auth?.HasUnresolvedPermissionPaths == true,
            endpoint.ExplicitPermission,
            endpoint.TypeName,
            endpoint.Namespace,
            endpoint.QueryBoundaryType,
            inputs.Boundaries.AsImmutableArray(),
            PermissionCatalogLookup.Index(inputs.Catalog));

        if (resolved is null)
            return endpoint;

        return endpoint with
        {
            Authorization = (auth ?? new AuthorizationModel()) with
            {
                IsRequired = true,
                RequiredPermissions = new EquatableArray<string>(ImmutableArray.Create(resolved.Value.Name))
            },
            PermissionSource = resolved.Value.Source
        };
    }

    /// <summary>
    ///     The names the switch gave this compilation's queries, for the manifest and the catalog. Empty
    ///     with the switch off: <see cref="EndpointModel.PermissionSource" /> is set by
    ///     <see cref="DeriveQueryPermission" /> alone.
    /// </summary>
    internal static EquatableArray<DerivedPermissionEntry> CollectDerivedQueryPermissions(
        ImmutableArray<EndpointModel> endpoints)
    {
        if (endpoints.IsDefaultOrEmpty)
            return EquatableArray<DerivedPermissionEntry>.Empty;

        var builder = ImmutableArray.CreateBuilder<DerivedPermissionEntry>();
        foreach (var endpoint in endpoints)
        {
            if (endpoint.PermissionSource is null || endpoint.Authorization is null)
                continue;

            foreach (var name in endpoint.Authorization.RequiredPermissions)
                builder.Add(new DerivedPermissionEntry(
                    endpoint.FullTypeName.Replace("global::", ""), name, endpoint.PermissionSource));
        }

        return new EquatableArray<DerivedPermissionEntry>(builder.ToImmutable());
    }

    /// <summary>
    ///     PRAG0421 for a query whose <c>[ExplicitPermission(Constant)]</c> the catalog cannot answer for —
    ///     the same report Actions makes for its own operations, because the fallback is the same: the
    ///     derived name, which is safe and is not what was written.
    /// </summary>
    private static void ReportUnresolvedExplicitQueryPermissions(
        SourceProductionContext context,
        (EndpointModel Endpoint, PermissionDerivationInputs Inputs) input)
    {
        var (endpoint, inputs) = input;
        if (!inputs.Enabled || !endpoint.IsQuery || endpoint.ExplicitPermission is null)
            return;

        ActionsFeature.ReportIfUnresolved(
            context, endpoint.ExplicitPermission, endpoint.TypeName, endpoint.Location,
            PermissionCatalogLookup.Index(inputs.Catalog));
    }
}

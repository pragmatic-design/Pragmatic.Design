using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Endpoints.Routes;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     Where a Create's 201 points: the read that answers at its own route plus <c>/{id}</c>.
/// </summary>
/// <remarks>
///     <para>
///         The one address the generator knows without guessing. <c>POST api/shipments</c> and
///         <c>GET api/shipments/{id}</c> on the same entity are the same resource and its instance; any
///         other pairing — a query on another route, one of several reads — would be a choice, and a
///         <c>Location</c> that names a route nothing serves is worse than none.
///     </para>
///     <para>
///         Decided here, with every endpoint of the compilation in hand, because the transform sees one
///         endpoint at a time. The header itself is built at runtime from the request path, so the
///         route prefix of the options and every group prefix are in it without the generator knowing
///         either.
///     </para>
/// </remarks>
internal static partial class EndpointsFeature
{
    /// <summary>
    ///     The reads a Create can point at: <c>{entity}|{route of the Single query without its /{id}}</c>.
    /// </summary>
    private static EquatableArray<string> CollectReadRoutes(ImmutableArray<EndpointModel> endpoints)
    {
        var builder = ImmutableArray.CreateBuilder<string>();

        foreach (var read in endpoints)
        {
            if (read is not { IsValid: true, IsQuery: true, QueryIsSingle: true, QueryEntityType: { } entity }
                || !string.Equals(read.HttpMethod, "Get", StringComparison.OrdinalIgnoreCase))
                continue;

            var route = EndpointRouteFacts.FullRoute(read).TrimEnd('/');
            var slash = route.LastIndexOf('/');
            if (slash < 0 || KeyParameterOf(read, route.Substring(slash + 1)) is null)
                continue;

            builder.Add(ReadRouteKey(entity, route.Substring(0, slash)));
        }

        return builder.ToImmutable();
    }

    /// <summary>The model with <see cref="EndpointModel.LocationFromReadRoute" /> set when its read exists.</summary>
    private static EndpointModel WithReadLocation(EndpointModel model, EquatableArray<string> readRoutes)
    {
        if (model is not { IsMutation: true, MutationModeName: "Create", CreatedAtTemplate: null, MutationEntityType: { } entity }
            || model.ComputedSuccessStatusCode != 201)
            return model;

        var key = ReadRouteKey(entity, EndpointRouteFacts.FullRoute(model).TrimEnd('/'));
        return readRoutes.AsImmutableArray().Contains(key) ? model with { LocationFromReadRoute = true } : model;
    }

    /// <summary>
    ///     The route parameter in the last segment, when it is the only thing there and it binds the
    ///     query's <c>Id</c> — the key, which is the value a Create's <c>Location</c> carries.
    /// </summary>
    private static RouteParameterModel? KeyParameterOf(EndpointModel read, string lastSegment)
    {
        if (lastSegment.Length < 3 || lastSegment[0] != '{' || lastSegment[lastSegment.Length - 1] != '}')
            return null;

        // {id}, {id:guid}, {id?}: the name is what precedes the constraint or the optional marker.
        var name = lastSegment.Substring(1, lastSegment.Length - 2).Split(':')[0].TrimEnd('?');

        var parameter = read.RouteParameters.AsImmutableArray()
            .FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        return parameter is not null && string.Equals(parameter.PropertyName, "Id", StringComparison.Ordinal)
            ? parameter
            : null;
    }

    private static string ReadRouteKey(string entity, string route)
        => $"{entity}|{route.ToLowerInvariant()}";
}

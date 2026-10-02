using Pragmatic.SourceGenerator.Features.Endpoints.Models;

// ReSharper disable once CheckNamespace
namespace Pragmatic.SourceGenerator.Features.Endpoints.Routes;

/// <summary>
///     Route/boundary/name derivation shared by the ApiRoutes template and the endpoint
///     contract — mirrors the manifest's OperationId conventions so all compile-time
///     surfaces (manifest, contract, routes, test client) agree.
/// </summary>
internal static class EndpointRouteFacts
{
    /// <summary>Group prefix + endpoint route, always with a leading slash.</summary>
    public static string FullRoute(EndpointModel ep)
    {
        var groupPrefix = ResolveGroupPrefix(ep.Group);
        var route = ep.Route ?? "";
        return string.IsNullOrEmpty(groupPrefix)
            ? "/" + route.TrimStart('/')
            : groupPrefix.TrimEnd('/') + "/" + route.TrimStart('/');
    }

    /// <summary>Boundary name from the group tag or the second namespace segment.</summary>
    public static string? Boundary(EndpointModel ep)
    {
        if (ep.Group?.Tag is { } tag)
            return tag;

        var parts = ep.Namespace.Split('.');
        return parts.Length >= 2 ? parts[1] : parts.Length == 1 ? parts[0] : null;
    }

    /// <summary>
    ///     Operation name for generated members: the endpoint Name, or the type name with
    ///     the Endpoint/Action/Mutation/Query suffix stripped (CreateReservationAction → CreateReservation).
    /// </summary>
    public static string OperationName(EndpointModel ep)
    {
        if (!string.IsNullOrEmpty(ep.Name))
            return ep.Name!;

        var name = ep.TypeName;
        foreach (var suffix in new[] { "Endpoint", "Mutation", "Action", "Query" })
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - suffix.Length);

        return name;
    }

    private static string ResolveGroupPrefix(EndpointGroupModel? group)
    {
        if (group is null) return "";
        var parentPrefix = ResolveGroupPrefix(group.Parent);
        var thisPrefix = group.RoutePrefix ?? "";
        return string.IsNullOrEmpty(parentPrefix)
            ? thisPrefix
            : parentPrefix.TrimEnd('/') + "/" + thisPrefix.TrimStart('/');
    }
}

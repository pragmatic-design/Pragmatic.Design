namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Promotes a package DomainAction to an HTTP endpoint with a route group.
/// </summary>
/// <typeparam name="TAction">The DomainAction type to expose.</typeparam>
/// <typeparam name="TGroup">The endpoint group type for route prefixing.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExposeEndpointAttribute<TAction, TGroup>(HttpVerb method, string route) : Attribute
    where TAction : class
    where TGroup : class
{
    /// <summary>The HTTP method.</summary>
    public HttpVerb Method { get; } = method;

    /// <summary>The route template, relative to the group's route prefix.</summary>
    public string Route { get; } = route;

    /// <summary>Optional endpoint name for route generation.</summary>
    public string? Name { get; set; }

    /// <summary>Additional permissions (additive with action's own).</summary>
    public string[]? AdditionalPermissions { get; set; }

    /// <summary>Bypasses all permission requirements.</summary>
    public bool AllowAnonymous { get; set; }
}

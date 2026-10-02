namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Promotes a DomainAction to an HTTP endpoint from the module that composes it.
///     Applied on a <c>[Module]</c> class to declare which actions are exposed as endpoints.
///     The source generator creates endpoint handlers in the host assembly.
/// </summary>
/// <remarks>
///     <para>
///         Unlike <see cref="EndpointAttribute" /> (which goes on the action class itself),
///         this attribute goes on the composing module class. This gives the consumer full control
///         over routing, permissions, and which actions are publicly exposed.
///     </para>
///     <para>
///         The action is a <c>DomainAction</c>, <c>VoidDomainAction</c> or <c>Mutation</c>. Its usual
///         source is one folded in by <c>[UsePackage&lt;T&gt;]</c> on the same module — a package's
///         actions carry no <c>[Endpoint]</c> of their own, because what they publish is the consumer's
///         to decide. ⚠️ Nothing restricts it to those: an action the module declares itself works the
///         same way, and is mapped on the host root rather than under the package's route prefix.
///     </para>
/// </remarks>
/// <typeparam name="TAction">The DomainAction type to expose as an endpoint.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExposeEndpointAttribute<TAction>(HttpVerb method, string route) : Attribute
    where TAction : class
{
    /// <summary>The HTTP method.</summary>
    public HttpVerb Method { get; } = method;

    /// <summary>
    ///     The route template — relative to the package's route prefix when the action came from a
    ///     package, and to the host root otherwise.
    /// </summary>
    public string Route { get; } = route;

    /// <summary>Optional endpoint name for route generation.</summary>
    public string? Name { get; set; }

    /// <summary>
    ///     Additional permissions required beyond those defined on the action.
    ///     Combined with the action's own <c>[RequirePermission]</c> (additive).
    /// </summary>
    public string[]? AdditionalPermissions { get; set; }

    /// <summary>
    ///     If true, bypasses all permission requirements (including the action's own).
    /// </summary>
    public bool AllowAnonymous { get; set; }
}

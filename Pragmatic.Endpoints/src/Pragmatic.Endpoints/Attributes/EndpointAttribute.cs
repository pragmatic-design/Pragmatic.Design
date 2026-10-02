using Pragmatic.Endpoints.Base;

namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Marks a class as a REST endpoint with the specified HTTP method and route.
/// </summary>
/// <remarks>
///     <para>
///         Apply this attribute to classes that inherit from <see cref="Endpoint{TResponse}" />,
///         <see cref="VoidEndpoint" />, or <c>DomainAction</c> to expose them as REST endpoints.
///     </para>
///     <para>
///         The source generator will create endpoint registration code, body DTOs,
///         and OpenAPI metadata automatically.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/users/{id}")]
/// public partial class GetUser : Endpoint&lt;UserResponse, NotFoundError&gt;
/// {
///     public required Guid Id { get; init; }
/// 
///     public override async Task&lt;Result&lt;UserResponse, NotFoundError&gt;&gt; HandleAsync(CancellationToken ct)
///     {
///         // Implementation
///     }
/// }
/// </code>
/// </example>
/// <remarks>
///     <para>
///         <b>Also on a static specification.</b> A <c>[Query]</c> on a static member returning
///         <c>Specification&lt;TEntity&gt;</c> derives a query type; this attribute beside it gives that
///         derived query its route. It is the only member target that means anything: on a member the
///         derivation does not read, the declaration reaches nobody and <c>PRAG0525</c> says so.
///     </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property,
    Inherited = false)]
public sealed class EndpointAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EndpointAttribute" /> class.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="route">The route template.</param>
    public EndpointAttribute(HttpVerb method, string route)
    {
        Method = method;
        Route = route;
    }

    /// <summary>
    ///     Gets the HTTP method for this endpoint.
    /// </summary>
    public HttpVerb Method { get; }

    /// <summary>
    ///     Gets the route template for this endpoint.
    /// </summary>
    /// <remarks>
    ///     Supports route parameters like <c>{id}</c> which are automatically bound
    ///     to properties with matching names.
    /// </remarks>
    public string Route { get; }

    /// <summary>
    ///     Gets or sets the endpoint name for route generation.
    /// </summary>
    /// <remarks>
    ///     If not specified, the class name is used.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    ///     Binds a single <c>[FromBody]</c> property directly, instead of wrapping it in an envelope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A hand-written endpoint always gets a generated request record around its
    ///         <c>[FromBody]</c> properties, even when there is exactly one — so the body it accepts is
    ///         <c>{"patch": {…}}</c> rather than the document itself. Mutations and domain actions have
    ///         never done that; endpoints did, and PATCH has no declarative form, so every patch
    ///         endpoint took a patch document nested in a field and a client written against the HTTP
    ///         semantics got 400.
    ///     </para>
    ///     <para>
    ///         Opt-in rather than the default, because the envelope is the published contract of every
    ///         endpoint that already exists: turning it off silently would change the body they accept.
    ///         Set it on a new endpoint, or on an old one together with its callers.
    ///     </para>
    ///     <para>
    ///         Ignored unless the endpoint has exactly one <c>[FromBody]</c> property and that property
    ///         is a complex type — a scalar cannot be a request body on its own.
    ///     </para>
    /// </remarks>
    public bool BindBodyDirectly { get; set; }
}
namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Defines an endpoint group with a shared route prefix and configuration.
/// </summary>
/// <remarks>
///     <para>
///         Apply this attribute to a class to create an endpoint group. An endpoint joins it with
///         <see cref="EndpointGroupAttribute{TGroup}" /> — the same attribute, one type argument — which
///         is why the group cannot be <c>static</c>: a static class is not allowed as a type argument
///         (CS0718). Declare it <c>sealed</c>, as the example does.
///     </para>
///     <para>
///         Nesting works the same way: a group that carries both declares its own prefix and the
///         parent it hangs from. There is no separate <c>Parent</c> property, because belonging is one
///         idea and had no business having two spellings.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [EndpointGroup("/api/v1/orders", Tag = "Orders")]
/// public sealed class OrdersGroup;
///
/// // Runtime options for the group are set in startup, keyed by the class name without "Group":
/// // options.ConfigureGroup("Orders", g => g.RequireAuthorization = true);
///
/// [Endpoint(HttpVerb.Post, "/")]
/// [EndpointGroup&lt;OrdersGroup&gt;]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EndpointGroupAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="EndpointGroupAttribute" /> class.
    /// </summary>
    /// <param name="routePrefix">The route prefix for the group.</param>
    public EndpointGroupAttribute(string routePrefix)
    {
        RoutePrefix = routePrefix;
    }

    /// <summary>
    ///     Gets the route prefix for all endpoints in this group.
    /// </summary>
    public string RoutePrefix { get; }

    /// <summary>
    ///     Gets or sets the OpenAPI tag for this group.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    ///     Gets or sets the API version for this group.
    /// </summary>
    public string? Version { get; set; }

}
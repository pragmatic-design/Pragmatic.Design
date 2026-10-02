namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies tags for an endpoint in OpenAPI documentation.
/// </summary>
/// <remarks>
///     <para>
///         Tags are used to group endpoints in OpenAPI/Swagger UI.
///         An endpoint can have multiple tags.
///     </para>
///     <para>
///         When using <see cref="EndpointGroupAttribute" />, the group's Tag
///         is automatically applied unless overridden with this attribute.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/orders/{id}")]
/// [ApiTags("Orders", "Customer Portal")]
/// public partial class GetOrder : Endpoint&lt;OrderDto, NotFoundError&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApiTagsAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ApiTagsAttribute" /> class.
    /// </summary>
    /// <param name="tags">The tags for the endpoint.</param>
    public ApiTagsAttribute(params string[] tags)
    {
        Tags = tags;
    }

    /// <summary>
    ///     Gets the tags for this endpoint.
    /// </summary>
    public string[] Tags { get; }
}
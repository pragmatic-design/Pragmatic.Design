namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies the detailed description for an endpoint in OpenAPI documentation.
/// </summary>
/// <remarks>
///     <para>
///         The description appears as the operation description in OpenAPI/Swagger UI.
///         Can include markdown formatting.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// [ApiSummary("Place a new order")]
/// [ApiDescription("""
///     Creates a new order for the authenticated customer.
/// 
///     ## Requirements
///     - Customer must have a valid shipping address
///     - At least one item must be in cart
/// 
///     ## Side Effects
///     - Sends confirmation email
///     - Reserves inventory
///     """)]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApiDescriptionAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ApiDescriptionAttribute" /> class.
    /// </summary>
    /// <param name="description">The description text for the endpoint.</param>
    public ApiDescriptionAttribute(string description)
    {
        Description = description;
    }

    /// <summary>
    ///     Gets the description text.
    /// </summary>
    public string Description { get; }
}
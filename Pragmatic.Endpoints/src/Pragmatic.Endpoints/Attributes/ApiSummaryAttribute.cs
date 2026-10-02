namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies the summary for an endpoint in OpenAPI documentation.
/// </summary>
/// <remarks>
///     <para>
///         The summary appears as the operation title in OpenAPI/Swagger UI.
///         Keep it short and descriptive.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Post, "/api/orders")]
/// [ApiSummary("Place a new order")]
/// public partial class PlaceOrder : DomainAction&lt;OrderId&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ApiSummaryAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ApiSummaryAttribute" /> class.
    /// </summary>
    /// <param name="summary">The summary text for the endpoint.</param>
    public ApiSummaryAttribute(string summary)
    {
        Summary = summary;
    }

    /// <summary>
    ///     Gets the summary text.
    /// </summary>
    public string Summary { get; }
}
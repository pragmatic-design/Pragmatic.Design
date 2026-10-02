namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Specifies the API version for an endpoint.
/// </summary>
/// <remarks>
///     <para>
///         The version is applied to the route via the configured versioning strategy
///         (route prefix, query parameter, or header).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Version in route: /api/v2/orders
/// [Endpoint(HttpVerb.Get, "/orders")]
/// [ApiVersion("2.0")]
/// public partial class GetOrdersV2 : Endpoint&lt;OrderListDto&gt; { }
/// 
/// // Multiple versions supported
/// [Endpoint(HttpVerb.Get, "/orders")]
/// [ApiVersion("1.0", Deprecated = true)]
/// [ApiVersion("2.0")]
/// public partial class GetOrders : Endpoint&lt;OrderListDto&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class ApiVersionAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ApiVersionAttribute" /> class.
    /// </summary>
    /// <param name="version">The version string (e.g., "1.0", "2.0").</param>
    public ApiVersionAttribute(string version)
    {
        Version = version;
    }

    /// <summary>
    ///     Gets the version string.
    /// </summary>
    public string Version { get; }

    /// <summary>
    ///     Gets or sets whether this version is deprecated.
    /// </summary>
    /// <remarks>
    ///     Deprecated versions are marked with appropriate headers and
    ///     documented as deprecated in OpenAPI.
    /// </remarks>
    public bool Deprecated { get; set; }

    /// <summary>
    ///     Gets or sets the deprecation message.
    /// </summary>
    public string? DeprecationMessage { get; set; }

    /// <summary>
    ///     Gets or sets the sunset date for this version.
    /// </summary>
    /// <remarks>
    ///     Format: "2024-12-31" (ISO 8601 date).
    /// </remarks>
    public string? SunsetDate { get; set; }
}
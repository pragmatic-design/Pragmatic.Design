using Pragmatic.Endpoints.Attributes;

namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Options for configuring endpoint groups programmatically.
/// </summary>
/// <remarks>
///     <para>
///         While <see cref="EndpointGroupAttribute" /> configures groups declaratively,
///         this class allows programmatic configuration at startup.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// services.Configure&lt;EndpointGroupOptions&gt;("Orders", options =>
/// {
///     options.RoutePrefix = "/api/v1/orders";
///     options.Tags.Add("Orders");
///     options.RequireAuthorization = true;
/// });
/// </code>
/// </example>
public sealed class EndpointGroupOptions
{
    /// <summary>
    ///     Gets or sets the route prefix for all endpoints in this group.
    /// </summary>
    public string? RoutePrefix { get; set; }

    /// <summary>
    ///     Gets the OpenAPI tags for endpoints in this group.
    /// </summary>
    public List<string> Tags { get; } = [];

    /// <summary>
    ///     Gets or sets whether authorization is required for all endpoints in this group.
    /// </summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>
    ///     Gets or sets the authorization policy name for endpoints in this group.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    ///     Gets the required permissions for all endpoints in this group (AND logic).
    /// </summary>
    public List<string> RequiredPermissions { get; } = [];

    /// <summary>
    ///     Gets or sets the API version for endpoints in this group.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    ///     Gets or sets the rate limit policy name for endpoints in this group.
    /// </summary>
    public string? RateLimitPolicy { get; set; }

    /// <summary>
    ///     Gets or sets the default response cache duration in seconds.
    /// </summary>
    public int? ResponseCacheDuration { get; set; }

    /// <summary>
    ///     Gets or sets whether to enable CORS for endpoints in this group.
    /// </summary>
    public bool EnableCors { get; set; }

    /// <summary>
    ///     Gets or sets the CORS policy name for endpoints in this group.
    /// </summary>
    public string? CorsPolicy { get; set; }
}
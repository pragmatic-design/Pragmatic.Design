namespace Pragmatic.Endpoints.Configuration;

/// <summary>
///     Global options for Pragmatic.Endpoints configuration.
/// </summary>
/// <example>
///     <code>
/// services.AddPragmaticEndpoints(options =>
/// {
///     options.RoutePrefix = "/api";
///     options.DefaultApiVersion = "1.0";
///     options.EnableOpenApi = true;
///     options.OpenApiTitle = "My API";
/// });
/// </code>
/// </example>
public sealed class PragmaticEndpointsOptions
{
    /// <summary>
    ///     Gets or sets the global route prefix for all endpoints.
    ///     Default is empty (no prefix).
    /// </summary>
    public string RoutePrefix { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the default API version.
    /// </summary>
    public string? DefaultApiVersion { get; set; }

    /// <summary>
    ///     Gets or sets whether the Pragmatic endpoints are published in the OpenAPI document.
    ///     Default is true.
    /// </summary>
    /// <remarks>
    ///     <c>false</c> leaves them out of the compile-time document <c>MapPragmaticOpenApi</c> serves:
    ///     it keeps its <c>info</c> and security schemes and has no operations. Nothing else changes:
    ///     the endpoints still answer, and a runtime document (<c>MapOpenApi</c>) keeps the
    ///     application's own endpoints.
    /// </remarks>
    public bool EnableOpenApi { get; set; } = true;

    /// <summary>
    ///     Gets or sets the OpenAPI document title.
    /// </summary>
    public string OpenApiTitle { get; set; } = "API";

    /// <summary>
    ///     Gets or sets the OpenAPI document description.
    /// </summary>
    public string? OpenApiDescription { get; set; }

    /// <summary>
    ///     Gets or sets the OpenAPI document version.
    /// </summary>
    public string OpenApiVersion { get; set; } = "1.0.0";

    /// <summary>
    ///     Gets or sets whether unannotated endpoints require an authenticated user by default
    ///     (applied once at the root route group). <b>Default is true</b> — secure-by-default: an endpoint
    ///     with no <c>[Authorize]</c>/<c>[RequirePermission]</c>/<c>[AllowAnonymous]</c> requires auth.
    ///     Opt a single endpoint out with <c>[AllowAnonymous]</c>. Set this to <c>false</c> for hosts that
    ///     don't run the authorization middleware (e.g. minimal/no-auth pipelines), otherwise the root
    ///     <c>RequireAuthorization()</c> throws "no middleware that supports authorization".
    /// </summary>
    public bool RequireAuthorizationByDefault { get; set; } = true;

    /// <summary>
    ///     Gets or sets the policy the root route group requires when
    ///     <see cref="RequireAuthorizationByDefault" /> is on. Unset, the root requires an authenticated
    ///     user and nothing more.
    /// </summary>
    /// <remarks>
    ///     The policy has to be registered with <c>AddAuthorization(o =&gt; o.AddPolicy(name, …))</c>. An
    ///     endpoint or a group that declares its own authorization adds to this one: it does not
    ///     replace it.
    /// </remarks>
    public string? DefaultAuthorizationPolicy { get; set; }

    /// <summary>
    ///     Gets or sets whether to enable response compression.
    ///     Default is false (uses global middleware if configured).
    /// </summary>
    public bool EnableResponseCompression { get; set; }

    /// <summary>
    ///     Gets or sets whether to use camelCase for JSON serialization.
    ///     Default is true.
    /// </summary>
    public bool UseCamelCaseJson { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to include stack traces in error responses.
    ///     Default is <c>false</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <strong>Never enable in production.</strong> Stack traces expose internal implementation details
    ///         and can be a security risk.
    ///     </para>
    ///     <para>
    ///         Guard this with an environment check:
    ///         <code>
    /// options.IncludeStackTraceInErrors = app.Environment.IsDevelopment();
    ///         </code>
    ///     </para>
    /// </remarks>
    public bool IncludeStackTraceInErrors { get; set; }

    /// <summary>
    ///     Gets or sets the default maximum upload file size in bytes.
    ///     Applied to all <c>IFormFile</c> parameters unless overridden by <c>[MaxFileSize]</c> on the property.
    ///     Default is <c>null</c> (no global limit).
    /// </summary>
    public long? MaxUploadFileSize { get; set; }

    /// <summary>
    ///     Gets or sets the default allowed content types for file uploads.
    ///     Applied to all <c>IFormFile</c> parameters unless overridden by <c>[AllowedContentTypes]</c> on the property.
    ///     Default is <c>null</c> (all content types accepted).
    /// </summary>
    /// <example>
    ///     <code>options.DefaultAllowedContentTypes = ["image/*", "application/pdf"];</code>
    /// </example>
    public string[]? DefaultAllowedContentTypes { get; set; }

    /// <summary>
    ///     Gets the global defaults for [Idempotent] endpoints.
    /// </summary>
    public IdempotencyOptions Idempotency { get; } = new();

    /// <summary>
    ///     Gets or sets the default rate limit policy name.
    /// </summary>
    public string? DefaultRateLimitPolicy { get; set; }

    /// <summary>
    ///     Gets or sets the HTTP status code returned when a rate limit is exceeded.
    ///     Default is 429 (Too Many Requests).
    /// </summary>
    public int RateLimitRejectionStatusCode { get; set; } = 429;

    /// <summary>
    ///     Gets the configured rate limiter policies.
    /// </summary>
    public Dictionary<string, RateLimiterConfiguration> RateLimiters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Configures a named rate limiter policy.
    ///     Supports fixed window, sliding window, token bucket, and concurrency limiters.
    /// </summary>
    /// <param name="policyName">The policy name (referenced by <c>[RateLimit(Policy = "...")]</c>).</param>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This options instance for chaining.</returns>
    /// <example>
    ///     <code>
    /// options.ConfigureRateLimiter("standard", limiter =>
    /// {
    ///     limiter.Strategy = RateLimiterStrategy.SlidingWindow;
    ///     limiter.PermitLimit = 100;
    ///     limiter.Window = TimeSpan.FromMinutes(1);
    ///     limiter.SegmentsPerWindow = 6;
    /// });
    ///     </code>
    /// </example>
    public PragmaticEndpointsOptions ConfigureRateLimiter(string policyName, Action<RateLimiterConfiguration> configure)
    {
        var config = new RateLimiterConfiguration();
        configure(config);
        RateLimiters[policyName] = config;
        return this;
    }

    /// <summary>
    ///     Gets the configured endpoint groups.
    /// </summary>
    public Dictionary<string, EndpointGroupOptions> Groups { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Configures a named endpoint group.
    /// </summary>
    /// <param name="groupName">The group name.</param>
    /// <param name="configure">The configuration action.</param>
    /// <returns>This options instance for chaining.</returns>
    public PragmaticEndpointsOptions ConfigureGroup(string groupName, Action<EndpointGroupOptions> configure)
    {
        if (!Groups.TryGetValue(groupName, out var options))
        {
            options = new EndpointGroupOptions();
            Groups[groupName] = options;
        }

        configure(options);
        return this;
    }
}
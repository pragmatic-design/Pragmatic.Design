namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Configures response caching for an endpoint.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="ResponseCacheLocation.Any" /> (the default) generates an ASP.NET output-cache policy:
///         the generated host adds the output cache for it, and it keeps anonymous answers only —
///         ASP.NET's default policy never caches an authenticated request, whatever the vary rules
///         (<c>PRAG0554</c> on a route that requires authentication). <see cref="ResponseCacheLocation.Client" /> generates
///         <c>Cache-Control: private</c> instead, for a signed-in user's own data.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Cache for 5 minutes
/// [Endpoint(HttpVerb.Get, "/api/products")]
/// [ResponseCache(Duration = 300)]
/// public partial class GetProducts : Endpoint&lt;ProductListDto&gt; { }
/// 
/// // Cache with vary by query
/// [Endpoint(HttpVerb.Get, "/api/products")]
/// [ResponseCache(Duration = 300, VaryByQueryKeys = new[] { "category", "page" })]
/// public partial class GetProducts : Endpoint&lt;ProductListDto&gt; { }
/// 
/// // Private cache (per-user)
/// [Endpoint(HttpVerb.Get, "/api/user/preferences")]
/// [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
/// public partial class GetUserPreferences : Endpoint&lt;PreferencesDto&gt; { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResponseCacheAttribute : Attribute
{
    /// <summary>
    ///     Gets or sets the duration in seconds for which the response should be cached.
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    ///     Gets or sets the location where the response can be cached.
    /// </summary>
    public ResponseCacheLocation Location { get; set; } = ResponseCacheLocation.Any;

    /// <summary>
    ///     Gets or sets whether to disable caching entirely.
    /// </summary>
    public bool NoStore { get; set; }

    /// <summary>
    ///     Gets or sets the query string keys to vary the cache by.
    /// </summary>
    public string[]? VaryByQueryKeys { get; set; }

    /// <summary>
    ///     Gets or sets the header names to vary the cache by.
    /// </summary>
    public string[]? VaryByHeaders { get; set; }

    /// <summary>
    ///     Gets or sets a named cache profile to use.
    /// </summary>
    public string? Profile { get; set; }
}
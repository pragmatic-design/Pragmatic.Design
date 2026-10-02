namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Binds a property value from a request cookie.
/// </summary>
/// <remarks>
///     <para>
///         ASP.NET Core Minimal APIs don't support cookie binding natively, so the source
///         generator reads the cookie from <c>HttpContext.Request.Cookies</c> inside the
///         handler body. Non-string types are parsed automatically.
///     </para>
///     <para>
///         Supported types: <c>string</c>, <c>Guid</c>, <c>int</c>, <c>long</c>,
///         <c>bool</c>, <c>DateTimeOffset</c>. Cookie values are used as-is (no URL decoding),
///         consistent with [FromHeader].
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Endpoint(HttpVerb.Get, "/api/preferences")]
/// public partial class GetPreferences : Endpoint&lt;PreferencesDto&gt;
/// {
///     [FromCookie("session-hint", IsRequired = false)]
///     public string? SessionHint { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromCookieAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance binding from a cookie named after the property.
    /// </summary>
    public FromCookieAttribute()
    {
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="FromCookieAttribute" /> with the specified cookie name.
    /// </summary>
    /// <param name="cookieName">The cookie name to bind from.</param>
    public FromCookieAttribute(string cookieName) => CookieName = cookieName;

    /// <summary>
    ///     Gets the cookie name to bind from; null uses the property name.
    /// </summary>
    public string? CookieName { get; }

    /// <summary>
    ///     Gets or sets whether the cookie is required. Defaults to <c>true</c>.
    ///     When required, the endpoint returns 400 if the cookie is missing (a cookie is
    ///     request input, not an authentication concern).
    /// </summary>
    public bool IsRequired { get; set; } = true;
}

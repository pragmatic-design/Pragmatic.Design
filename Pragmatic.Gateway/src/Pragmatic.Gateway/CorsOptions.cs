namespace Pragmatic.Gateway;

/// <summary>
///     CORS policy configuration.
///     <para>
///         <b>Security note</b>: Using a wildcard (<c>"*"</c>) in <see cref="Origins" /> or
///         <see cref="Headers" /> is forbidden when <see cref="AllowCredentials" /> is
///         <see langword="true" /> — the browser rejects such a policy and ASP.NET Core will
///         throw at startup. List explicit origins when credentials must be allowed.
///     </para>
/// </summary>
public sealed class CorsOptions
{
    /// <summary>
    ///     Allowed origins. Use <c>["*"]</c> to allow any origin <em>without</em>
    ///     credentials, or list explicit origins when <see cref="AllowCredentials" /> is
    ///     <see langword="true" />.
    /// </summary>
    public List<string> Origins { get; set; } = ["*"];

    /// <summary>Allowed HTTP methods. Default: GET, POST, PUT, PATCH, DELETE, OPTIONS.</summary>
    public List<string> Methods { get; set; } = ["GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS"];

    /// <summary>
    ///     Allowed request headers. Use <c>["*"]</c> to allow any header <em>without</em>
    ///     credentials. List explicit headers when <see cref="AllowCredentials" /> is
    ///     <see langword="true" />.
    /// </summary>
    public List<string> Headers { get; set; } = ["*"];

    /// <summary>
    ///     Whether to allow credentials (cookies, Authorization header).
    ///     Cannot be combined with a wildcard origin or wildcard headers.
    /// </summary>
    public bool AllowCredentials { get; set; }
}

namespace Pragmatic.Composition.Configuration;

/// <summary>
///     The response headers a browser needs in order to enforce anything on the client side.
/// </summary>
/// <remarks>
///     Defaults are the strict ones. A header that has to be opted into is a header nobody sets, and
///     these exist to be present on every response — including error responses, which is where they are
///     most often missed.
/// </remarks>
public sealed class SecurityHeadersOptions
{
    /// <summary>Whether to emit the headers at all. On by default.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     <c>Content-Security-Policy</c>. Defaults to a policy that permits nothing it is not told to.
    /// </summary>
    /// <remarks>
    ///     An API serves data, not documents, so denying scripts and frames outright costs nothing and
    ///     removes the class of attacks that rely on a response being interpreted as a page. An
    ///     application that serves HTML must replace this — the default is deliberately too strict to
    ///     leave in place by accident.
    /// </remarks>
    public string? ContentSecurityPolicy { get; set; } =
        "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";

    /// <summary>
    ///     <c>Strict-Transport-Security</c> max-age, in seconds. One year by default; zero omits the header.
    /// </summary>
    /// <remarks>
    ///     Only sent over HTTPS. Sending it over plain HTTP is meaningless — a browser ignores it — and
    ///     would pin a policy learned from a connection that could have been tampered with.
    /// </remarks>
    public int StrictTransportSecurityMaxAgeSeconds { get; set; } = 31_536_000;

    /// <summary>Whether the HSTS policy covers subdomains.</summary>
    public bool StrictTransportSecurityIncludeSubDomains { get; set; } = true;

    /// <summary><c>X-Content-Type-Options: nosniff</c>. On by default.</summary>
    /// <remarks>
    ///     Stops a browser from guessing a content type it was not given. Without it, a stored file
    ///     served as one type can be re-interpreted as another — how an upload becomes a script.
    /// </remarks>
    public bool NoSniff { get; set; } = true;

    /// <summary><c>X-Frame-Options</c>. <c>DENY</c> by default; null omits the header.</summary>
    /// <remarks>
    ///     Redundant with <c>frame-ancestors</c> for current browsers, and kept because it is what older
    ///     ones honour. Belt and braces costs one header.
    /// </remarks>
    public string? FrameOptions { get; set; } = "DENY";

    /// <summary><c>Referrer-Policy</c>. Null omits the header.</summary>
    /// <remarks>
    ///     The default stops a URL — which routinely carries identifiers — from leaking to another origin
    ///     in the <c>Referer</c> header.
    /// </remarks>
    public string? ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>Extra headers to emit, or overrides for the ones above.</summary>
    public IDictionary<string, string> Additional { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

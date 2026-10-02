namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is a valid URL.
/// </summary>
/// <remarks>
///     <para>
///         By default, only HTTP and HTTPS schemes are allowed.
///         Use <see cref="AllowedSchemes" /> to allow additional schemes.
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateLinkRequest
/// {
///     [Required]
///     [Url]
///     public string WebsiteUrl { get; init; }
/// 
///     [Url(AllowedSchemes = new[] { "http", "https", "ftp" })]
///     public string FileUrl { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class UrlAttribute : ValidationAttribute
{
    private static readonly string[] DefaultSchemes = ["http", "https"];

    /// <summary>
    ///     Gets or sets the allowed URL schemes.
    ///     Default is <c>["http", "https"]</c>.
    /// </summary>
    public string[]? AllowedSchemes { get; set; }

    /// <summary>
    ///     Gets or sets whether to require an absolute URI.
    ///     Default is <c>true</c>.
    /// </summary>
    public bool RequireAbsolute { get; set; } = true;

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.url";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string url)
            return true;

        return IsValidUrl(url, AllowedSchemes, RequireAbsolute);
    }

    /// <summary>
    ///     Validates that a string is a valid URL.
    /// </summary>
    /// <param name="url">The URL string to validate.</param>
    /// <param name="allowedSchemes">Allowed URL schemes. If null, defaults to http and https.</param>
    /// <param name="requireAbsolute">Whether to require an absolute URI.</param>
    /// <returns><c>true</c> if the URL is valid or empty; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation.
    /// </remarks>
    public static bool IsValidUrl(string? url, string[]? allowedSchemes = null, bool requireAbsolute = true)
    {
        if (string.IsNullOrEmpty(url))
            return true;

        // On Linux and macOS TryCreate(…, Absolute) turns a path such as "/docs/page" into
        // file:///docs/page on its own (on Windows it fails): that is a relative URL, not an absolute one
        // with a file scheme, or the relative branch below is never reached there.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.IsFile && !url.TrimStart().StartsWith("file:", StringComparison.OrdinalIgnoreCase)))
        {
            if (requireAbsolute)
                return false;

            // Try relative
            return Uri.TryCreate(url, UriKind.Relative, out _);
        }

        // Check scheme
        var schemes = allowedSchemes ?? DefaultSchemes;
        return schemes.Any(s => uri.Scheme.Equals(s, StringComparison.OrdinalIgnoreCase));
    }
}
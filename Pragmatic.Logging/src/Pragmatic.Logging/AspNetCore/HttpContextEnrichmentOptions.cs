namespace Pragmatic.Logging.AspNetCore;

/// <summary>
///     What the HTTP request contributes to the logging context.
/// </summary>
/// <remarks>
///     Named for what it configures, not <c>PragmaticLoggingOptions</c>: that is the simple name of the
///     type in <c>Pragmatic.Logging.Configuration</c> that carries minimum level, context and audit.
///     Two unrelated types a <c>using</c> away from each other would make which one a caller got depend
///     on which namespace was imported, with no compiler error, because both would be valid targets
///     for <c>services.AddPragmaticLogging(o =&gt; …)</c>.
/// </remarks>
public sealed class HttpContextEnrichmentOptions
{
    /// <summary>
    ///     Gets or sets whether to enable HTTP context enrichment.
    /// </summary>
    public bool EnableHttpContextEnrichment { get; set; } = true;

    /// <summary>
    ///     Gets or sets whether to enable correlation ID tracking.
    /// </summary>
    public bool EnableCorrelationIdTracking { get; set; } = true;

    /// <summary>
    ///     Gets or sets the header name for correlation ID.
    /// </summary>
    public string CorrelationIdHeaderName { get; set; } = CorrelationIdProvider.CorrelationIdHeaderName;

    /// <summary>
    ///     Gets or sets whether to include sensitive headers in context.
    /// </summary>
    public bool IncludeSensitiveHeaders { get; set; }

    /// <summary>
    ///     Gets or sets whether the authenticated user's email claim is included in context.
    ///     PII: disabled by default to avoid leaking email addresses to every log sink.
    /// </summary>
    public bool IncludeUserEmail { get; set; }

    /// <summary>
    ///     Gets or sets whether the client's remote IP address is included in context.
    ///     PII / GDPR: disabled by default. When enabled, <see cref="MaskClientIpAddress" />
    ///     controls whether the address is masked before logging.
    /// </summary>
    public bool IncludeRemoteIpAddress { get; set; }

    /// <summary>
    ///     Gets or sets whether an included remote IP address is masked (last octet of IPv4 /
    ///     low bits of IPv6 zeroed). Defaults to <c>true</c>. Only relevant when
    ///     <see cref="IncludeRemoteIpAddress" /> is enabled.
    /// </summary>
    public bool MaskClientIpAddress { get; set; } = true;

    /// <summary>
    ///     Gets or sets custom headers to include in context.
    /// </summary>
    public List<string> CustomHeaders { get; set; } = new();
}

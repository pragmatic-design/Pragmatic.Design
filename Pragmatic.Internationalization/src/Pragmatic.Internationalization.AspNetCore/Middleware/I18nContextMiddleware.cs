using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Diagnostics;
using Pragmatic.Internationalization.Types;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Internationalization.AspNetCore.Middleware;

/// <summary>
///     Middleware that populates <see cref="I18NContext"/> from the request using the provider system.
/// </summary>
/// <remarks>
///     <para>
///         This middleware resolves culture configuration from all registered
///         <see cref="II18NConfigProvider"/> instances and sets the <see cref="I18NContext"/>.
///     </para>
///     <para>
///         Culture is determined from providers in priority order (highest first):
///         <list type="number">
///             <item>RequestConfigProvider (priority 300) - query string, Accept-Language</item>
///             <item>UserConfigProvider (priority 200) - user preferences from DB</item>
///             <item>TenantConfigProvider (priority 100) - tenant settings from DB</item>
///             <item>SystemConfigProvider (priority 0) - from appsettings.json</item>
///         </list>
///     </para>
/// </remarks>
public partial class I18NContextMiddleware
{

    private readonly ILogger<I18NContextMiddleware>? _logger;
    private readonly RequestDelegate _next;

    /// <summary>
    ///     Creates a new instance of the middleware.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public I18NContextMiddleware(
        RequestDelegate next,
        ILogger<I18NContextMiddleware>? logger = null)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    ///     Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <param name="resolver">The configuration resolver.</param>
    /// <param name="options">The internationalization options (for the configurable query-string key).</param>
    public async Task InvokeAsync(HttpContext context, I18NConfigResolver resolver, IOptions<I18NOptions> options)
    {
        using var activity = I18NDiagnostics.ActivitySource.StartActivity("I18N.Context.Resolve");

        // Resolve configuration from all providers (may throw if not configured)
        var config = resolver.Resolve();

        // Check for request-level override (query string)
        var requestCulture = GetRequestCultureOverride(context, resolver, options.Value.QueryStringKey);
        if (requestCulture.HasValue)
        {
            config = config with { DefaultUICulture = requestCulture };
            activity?.SetTag(I18NTags.Source, "query_string");
        }
        else
        {
            // Check Accept-Language header as fallback
            var acceptLanguageCulture = GetAcceptLanguageCulture(context, resolver);
            if (acceptLanguageCulture.HasValue)
            {
                config = config with { DefaultUICulture = acceptLanguageCulture };
                activity?.SetTag(I18NTags.Source, "accept_language");
            }
            else
            {
                activity?.SetTag(I18NTags.Source, "provider");
            }
        }

        activity?.SetTag(I18NTags.UICulture, config.DefaultUICulture?.Code ?? "unknown");
        activity?.SetTag(I18NTags.DataCulture, config.DefaultDataCulture?.Code ?? "unknown");

        if (_logger is not null)
        {
            LogCultureResolved(_logger,
                config.DefaultUICulture?.Code ?? "unknown",
                config.DefaultDataCulture?.Code ?? "unknown");
        }

        // Capture the inbound context AND thread cultures before mutating them, then restore
        // after the request. SetFromConfig() calls SyncThreadCulture(), so a bare Clear() would
        // leave the pooled thread's CurrentUICulture/CurrentCulture mutated and leak into the
        // next request on the same thread. Snapshot is taken only after resolve succeeds, so a
        // failing resolver never clobbers a valid parent context.
        var snapshot = I18NContext.Capture();
        I18NContext.SetFromConfig(config);
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }
    }

    private static CultureCode? GetRequestCultureOverride(HttpContext context, I18NConfigResolver resolver, string queryStringKey)
    {
        // Check query string for culture override (key is configurable via I18NOptions.QueryStringKey)
        if (context.Request.Query.TryGetValue(queryStringKey, out var queryValue) &&
            !string.IsNullOrEmpty(queryValue.FirstOrDefault()))
        {
            if (CultureCode.TryFromString(queryValue.First()!, out var culture))
            {
                // Validate against supported cultures
                var bestMatch = resolver.FindBestMatch(culture);
                if (bestMatch.HasValue)
                {
                    return bestMatch.Value;
                }
            }
        }

        return null;
    }

    private static CultureCode? GetAcceptLanguageCulture(HttpContext context, I18NConfigResolver resolver)
    {
        var acceptLanguage = context.Request.GetTypedHeaders().AcceptLanguage;
        if (acceptLanguage is null || acceptLanguage.Count == 0)
            return null;

        // Process in quality order (highest first)
        foreach (var lang in acceptLanguage.OrderByDescending(l => l.Quality ?? 1.0))
        {
            if (!lang.Value.HasValue)
                continue;

            var cultureString = lang.Value.Value;
            if (CultureCode.TryFromString(cultureString, out var culture))
            {
                // Find best match in supported cultures
                var bestMatch = resolver.FindBestMatch(culture);
                if (bestMatch.HasValue)
                {
                    return bestMatch.Value;
                }
            }
        }

        return null;
    }

    // LoggerMessage source-generated methods (zero allocation)

    [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved I18N context: UI={UICulture}, Data={DataCulture}")]
    private static partial void LogCultureResolved(ILogger logger, string uiCulture, string dataCulture);
}

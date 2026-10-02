using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.AspNetCore.Endpoints;

/// <summary>
///     Exposes translation data via HTTP endpoints for frontend consumption.
/// </summary>
/// <remarks>
///     <para>
///         Call <c>app.MapPragmaticTranslations()</c> to register the endpoint.
///         The endpoint aggregates all registered <see cref="ILocalizationProvider"/> instances
///         via <see cref="CompositeLocalizationProvider"/>.
///     </para>
///     <para>
///         Supports prefix filtering via <c>?prefix=booking.</c> query parameter
///         for lazy loading per boundary/feature.
///     </para>
/// </remarks>
public static class TranslationEndpoints
{
    /// <summary>
    ///     Maps the translation endpoint at the specified route prefix.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="routePrefix">Route prefix (default: "/api/i18n").</param>
    /// <param name="cacheDurationSeconds">Cache-Control max-age in seconds (default: 300 = 5 min). 0 to disable.</param>
    /// <returns>The route handler builder for further configuration.</returns>
    /// <example>
    /// <code>
    /// app.MapPragmaticTranslations();
    /// // GET /api/i18n/it-IT → { "culture": "it-IT", "translations": { ... } }
    /// // GET /api/i18n/it-IT?prefix=booking. → only keys starting with "booking."
    /// </code>
    /// </example>
    /// <remarks>
    ///     Mapped as a <c>RequestDelegate</c>, not as a handler <c>Delegate</c>: ASP.NET binds the
    ///     latter through a reflection-based factory that does not survive a Native AOT publish, and a
    ///     single endpoint mapped that way fails the construction of the WHOLE routing table — an app
    ///     whose own endpoints are fine would break just for calling this.
    /// </remarks>
    public static IEndpointConventionBuilder MapPragmaticTranslations(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/api/i18n",
        int cacheDurationSeconds = 300)
    {
        var handler = (string culture, string? prefix, HttpContext httpContext) =>
        {
            // Reject anything that is not a well-formed culture code before it reaches the
            // providers (which may resolve it against the filesystem). Returns 404, not 500.
            if (!CultureCode.TryFromString(culture, out _))
                return Results.NotFound();

            var provider = httpContext.RequestServices.GetService<CompositeLocalizationProvider>();
            if (provider is null)
                return Results.Problem(
                    "No localization providers registered. Call AddLocalizationProvider() or AddJsonTranslations() on I18NBuilder.",
                    statusCode: 500);

            var all = provider.GetAll(culture);

            // Filter by prefix if specified
            IReadOnlyDictionary<string, string> translations = all;
            if (!string.IsNullOrEmpty(prefix))
            {
                translations = all
                    .Where(kv => kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }

            if (cacheDurationSeconds > 0)
                httpContext.Response.Headers.CacheControl = $"public, max-age={cacheDurationSeconds}";

            return Results.Ok(new TranslationResponse(culture, translations));
        };

        var builder = endpoints.MapGet(routePrefix + "/{culture}", (RequestDelegate)(async httpContext =>
        {
            var culture = httpContext.Request.RouteValues.TryGetValue("culture", out var raw)
                ? raw?.ToString()
                : null;

            if (culture is null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var prefix = httpContext.Request.Query.TryGetValue("prefix", out var values) && values.Count > 0
                ? values[0]
                : null;

            await handler(culture, prefix, httpContext).ExecuteAsync(httpContext).ConfigureAwait(false);
        }));

        builder.WithName("GetTranslations");
        builder.WithTags("i18n");
        builder.WithMetadata(new Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(
            StatusCodes.Status200OK, typeof(TranslationResponse)));
        builder.WithMetadata(new Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(
            StatusCodes.Status500InternalServerError));

        return builder;
    }
}

/// <summary>
///     Response shape for the translation endpoint.
/// </summary>
internal sealed record TranslationResponse(
    string Culture,
    IReadOnlyDictionary<string, string> Translations);

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Configuration;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Emits the security response headers on every response.
///     Order 5 — before everything, so error responses carry them too.
/// </summary>
/// <remarks>
///     <para>
///         Running early is the point. Headers added late are missing from exactly the responses that
///         matter most: the ones produced by an exception handler or a short-circuiting middleware,
///         where a browser is most likely to be shown something unintended.
///     </para>
///     <para>
///         The headers are written when the response starts rather than after the pipeline returns —
///         once a response has begun, headers can no longer be changed, and a middleware that writes
///         directly would silently lose them.
///     </para>
/// </remarks>
public class SecurityHeadersStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 5;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetService<IOptions<SecurityHeadersOptions>>()?.Value
                      ?? new SecurityHeadersOptions();

        if (!options.Enabled)
            return;

        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                Apply(context, options);
                return Task.CompletedTask;
            });

            await next().ConfigureAwait(false);
        });
    }

    private static void Apply(HttpContext context, SecurityHeadersOptions options)
    {
        var headers = context.Response.Headers;

        // Never overwrite a header a handler set deliberately: an endpoint that serves HTML may need a
        // different policy from the API default, and silently replacing it would be the framework
        // overruling the person who knew the case.
        Set(headers, "X-Content-Type-Options", options.NoSniff ? "nosniff" : null);
        Set(headers, "X-Frame-Options", options.FrameOptions);
        Set(headers, "Referrer-Policy", options.ReferrerPolicy);
        Set(headers, "Content-Security-Policy", options.ContentSecurityPolicy);

        // HSTS only over HTTPS. A browser ignores it on a plain connection, and honouring it there
        // would mean pinning a policy learned from a channel that could have been tampered with.
        if (context.Request.IsHttps && options.StrictTransportSecurityMaxAgeSeconds > 0)
        {
            var value = $"max-age={options.StrictTransportSecurityMaxAgeSeconds}";
            if (options.StrictTransportSecurityIncludeSubDomains)
                value += "; includeSubDomains";

            Set(headers, "Strict-Transport-Security", value);
        }

        foreach (var (name, value) in options.Additional)
            Set(headers, name, value);
    }

    private static void Set(IHeaderDictionary headers, string name, string? value)
    {
        if (value is null || headers.ContainsKey(name))
            return;

        headers[name] = value;
    }
}

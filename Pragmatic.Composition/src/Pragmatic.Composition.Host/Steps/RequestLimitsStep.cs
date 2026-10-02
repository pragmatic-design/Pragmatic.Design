using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Http;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Applies request body size limits: per-endpoint from <see cref="MaxBodySizeMetadata" />
///     (emitted by [MaxBodySize]) with a global default from configuration
///     ("Pragmatic:RequestLimits:MaxBodySizeBytes").
///     Order 55 — after routing (50, endpoint resolved) and before the body is consumed.
///     Stateless by design: DI-registered steps only get ConfigurePipeline invoked
///     (CallConfigureServices covers compile-time-discovered steps with separate instances).
/// </summary>
public class RequestLimitsStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 55;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        // Read as a string and parsed here: ConfigurationBinder.GetValue<T> converts through a
        // TypeConverter it looks up by reflection, which is a trim/AOT warning for a value that is
        // one long.
        var configured = app.ApplicationServices.GetService<IConfiguration>()
            ?["Pragmatic:RequestLimits:MaxBodySizeBytes"];

        var globalLimit = long.TryParse(
            configured,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : (long?)null;

        app.Use(async (context, next) =>
        {
            // Endpoint metadata wins over the global default; GetMetadata<T> returns the LAST
            // entry, so an endpoint-level [MaxBodySize] overrides a group-level value.
            var limit = context.GetEndpoint()?.Metadata.GetMetadata<MaxBodySizeMetadata>()?.MaxBytes
                        ?? globalLimit;

            if (limit is { } maxBytes)
            {
                // Reject oversized declared bodies up front — this also enforces the limit on
                // test servers where the transport does not honor IHttpMaxRequestBodySizeFeature.
                if (context.Request.ContentLength > maxBytes)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }

                var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (feature is { IsReadOnly: false })
                    feature.MaxRequestBodySize = maxBytes;
            }

            await next().ConfigureAwait(false);
        });
    }
}

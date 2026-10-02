using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds CORS middleware to the pipeline.
///     Order 75 — runs after routing but before authentication.
/// </summary>
public class CorsStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 75;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        // app.UseCors() with no policy name applies the DEFAULT policy. If no default policy was
        // registered (AddCors without a default), the middleware is a silent no-op and CORS headers
        // never appear — a confusing class of "CORS just doesn't work" bug. Warn loudly here so the
        // misconfiguration is visible at startup instead of being discovered from the browser.
        var corsOptions = app.ApplicationServices.GetService<IOptions<CorsOptions>>()?.Value;
        var hasDefaultPolicy = corsOptions?.GetPolicy(corsOptions.DefaultPolicyName) is not null;
        if (!hasDefaultPolicy)
        {
            app.ApplicationServices.GetService<ILoggerFactory>()?
                .CreateLogger<CorsStep>()
                .LogWarning(
                    "CorsStep ran but no default CORS policy is registered; app.UseCors() will be a no-op " +
                    "and no CORS headers will be emitted. Configure a default policy (AddCors(o => o.AddDefaultPolicy(...))) " +
                    "or remove CORS from the pipeline.");
        }

        app.UseCors();
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     Adds CORS middleware to the pipeline.
///     Order 75 — runs after routing but before authentication.
/// </summary>
/// <remarks>
///     The generated host registers it for every host with endpoints, beside <see cref="ConfiguredCorsPolicy" /> (#748): <c>Cors:Origins</c>
///     in configuration is the whole declaration. A host with neither that section nor a default policy of its own gets no CORS middleware,
///     as before, and no warning: most hosts serve their own origin.
/// </remarks>
public class CorsStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 75;

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        // app.UseCors() with no policy name applies the DEFAULT policy, and with none registered it is a silent no-op. A Cors section that
        // produced no policy is a misconfiguration worth saying at startup; no section at all is the host that does not use CORS.
        var corsOptions = app.ApplicationServices.GetService<IOptions<CorsOptions>>()?.Value;
        if (corsOptions?.GetPolicy(corsOptions.DefaultPolicyName) is null)
        {
            var configured = app.ApplicationServices.GetService<IConfiguration>()?.GetSection(ConfiguredCorsPolicy.Section).Exists() is true;
            if (configured)
            {
                app.ApplicationServices.GetService<ILoggerFactory>()?
                    .CreateLogger<CorsStep>()
                    .LogWarning(
                        "A Cors section is configured but no default CORS policy was registered: no CORS headers will be emitted. " +
                        "Set Cors:Origins, or register a default policy (AddCors(o => o.AddDefaultPolicy(...))).");
            }

            return;
        }

        app.UseCors();
    }
}

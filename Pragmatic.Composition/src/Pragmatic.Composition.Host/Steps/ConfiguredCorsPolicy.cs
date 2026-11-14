using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Pragmatic.Composition.Steps;

/// <summary>
///     The default CORS policy of an application host, from configuration (#748): <c>Cors:Origins</c>, and <c>Cors:AllowCredentials</c>,
///     <c>Cors:Methods</c> and <c>Cors:Headers</c> beside it, as Pragmatic.Gateway reads its own under <c>Gateway:Cors</c>.
/// </summary>
/// <remarks>
///     <para>
///         <see cref="CorsStep" /> applied a default policy that no package registered, so an application host answered no preflight with
///         the headers a browser needs. No <c>Cors:Origins</c> registers no policy, which is what a host had before: no CORS.
///     </para>
///     <para>
///         An origin <c>"*"</c> allows any origin, and a method or a header list left out or <c>"*"</c> allows any. A wildcard origin with
///         credentials is refused at the first resolution of the options: the browser rejects that combination, and ASP.NET Core fails on it
///         with an opaque message.
///     </para>
///     <para>
///         Read value by value rather than bound: the configuration binder converts through reflection, a trim and AOT warning for a list of
///         strings.
///     </para>
/// </remarks>
public sealed class ConfiguredCorsPolicy(IConfiguration configuration) : IConfigureOptions<CorsOptions>
{
    /// <summary>The configuration section the policy is read from.</summary>
    public const string Section = "Cors";

    /// <inheritdoc />
    public void Configure(CorsOptions options)
    {
        var origins = Values("Origins");
        if (origins.Length == 0)
            return;

        var allowCredentials = string.Equals(configuration[$"{Section}:AllowCredentials"], "true", StringComparison.OrdinalIgnoreCase);
        if (allowCredentials && origins.Contains("*"))
            throw new InvalidOperationException(
                "Invalid CORS configuration: Cors:AllowCredentials cannot be combined with the origin '*'. List the origins, or turn credentials off.");

        var methods = Values("Methods");
        var headers = Values("Headers");
        options.AddDefaultPolicy(policy =>
        {
            if (origins.Contains("*"))
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(origins);

            if (methods.Length == 0 || methods.Contains("*"))
                policy.AllowAnyMethod();
            else
                policy.WithMethods(methods);

            if (headers.Length == 0 || headers.Contains("*"))
                policy.AllowAnyHeader();
            else
                policy.WithHeaders(headers);

            if (allowCredentials)
                policy.AllowCredentials();
        });
    }

    private string[] Values(string key)
        => configuration.GetSection($"{Section}:{key}").GetChildren()
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToArray();
}

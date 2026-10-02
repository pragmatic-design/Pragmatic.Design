using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition.Abstractions;

namespace Pragmatic.Identity;

/// <summary>
///     Puts <see cref="HeaderUserMiddleware" /> in front of the authentication middleware, so the
///     <c>X-User-*</c> headers become the current identity while running in Development.
/// </summary>
/// <remarks>
///     Order 50 — before <c>AuthenticationStep</c> (91), which is the whole point: the principal has
///     to exist before authorization looks at it.
///     <para>
///         The environment is read from the application services rather than remembered from
///         <see cref="ConfigureServices" />: the two methods run on different step instances (one on a
///         bootstrap provider, one on the final one), so state stored in the first is gone in the
///         second. A consumer lost time to a flag that was always false.
///     </para>
/// </remarks>
public sealed class DevelopmentIdentityStep : IStartupStep
{
    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
    }

    /// <inheritdoc />
    public void ConfigurePipeline(IApplicationBuilder app)
    {
        var environment = app.ApplicationServices.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment())
            app.UseMiddleware<HeaderUserMiddleware>();
    }
}

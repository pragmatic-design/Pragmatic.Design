using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Composition;

/// <summary>
///     Fluent builder for configuring Pragmatic module strategies at startup.
///     Each module package contributes extension methods (e.g., UseMultiTenancy, UseAuthentication).
///     IntelliSense shows only the modules referenced by the host project.
/// </summary>
/// <remarks>
///     <para>
///         Use in <c>PragmaticApp.RunAsync</c> to configure module strategies:
///     </para>
///     <code>
/// await PragmaticApp.RunAsync(args, app =>
/// {
///     app.UseMultiTenancy(mt => mt.UseHeader());
///     app.UseAuthentication(auth => auth.UseNoOp());
/// });
///     </code>
/// </remarks>
public interface IPragmaticBuilder
{
    /// <summary>
    ///     Gets the service collection for DI registration.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    ///     Gets the application configuration (appsettings.json, environment variables, etc.).
    /// </summary>
    IConfiguration Configuration { get; }

    /// <summary>
    ///     Gets the host environment (Development, Production, etc.).
    /// </summary>
    IHostEnvironment Environment { get; }
}

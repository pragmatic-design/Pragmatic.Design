using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Composition.Abstractions;

/// <summary>
///     Interface for module startup configuration.
///     Implementations are discovered automatically when marked with <see cref="Attributes.StartupStepAttribute" />.
/// </summary>
/// <remarks>
///     <para>
///         Modules implement this interface to configure services and the middleware pipeline.
///         The <see cref="Order" /> property determines execution order (lower = earlier).
///     </para>
///     <para>
///         Recommended order ranges:
///         <list type="bullet">
///             <item>0-99: Infrastructure (routing, compression, CORS)</item>
///             <item>100-499: Module steps (authentication, authorization, i18n)</item>
///             <item>500+: Consumer/application steps</item>
///         </list>
///     </para>
/// </remarks>
public interface IStartupStep
{
    /// <summary>
    ///     Gets the execution order. Lower values execute first.
    ///     Default is 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    ///     Configures services for the DI container.
    ///     Called during application build phase.
    /// </summary>
    void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Default: no-op
    }

    /// <summary>
    ///     Configures the HTTP request pipeline.
    ///     Called after application is built, before running.
    /// </summary>
    void ConfigurePipeline(IApplicationBuilder app)
    {
        // Default: no-op
    }
}
